"""Public GitHub release policy. No credentials or document data leave the service."""
import json
import re
import threading
import time
from urllib import request
from urllib.parse import urlsplit
from security_policy import POLICY_URL, fetch_policy

REPOSITORY = "NightmindOfficial/asn-labeler"
IMAGE = "ghcr.io/nightmindofficial/asn-reservation"


def version(value):
    if not isinstance(value, str) or not re.fullmatch(r"(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})", value):
        raise ValueError("Invalid release version")
    parts = tuple(map(int, value.split(".")))
    if max(parts) > 65534:
        raise ValueError("Invalid release version")
    return parts


def allowed_url(url):
    if url == POLICY_URL:
        return True
    parsed = urlsplit(url)
    if parsed.scheme != "https" or parsed.port not in (None, 443) or parsed.username or parsed.password:
        return False
    return ((parsed.hostname == "api.github.com" and parsed.path.startswith("/repos/" + REPOSITORY + "/")) or
            (parsed.hostname == "github.com" and parsed.path.startswith("/" + REPOSITORY + "/releases/download/")) or
            parsed.hostname in ("release-assets.githubusercontent.com", "objects.githubusercontent.com"))


class SafeRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if not allowed_url(newurl):
            raise ValueError("Untrusted release redirect")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def fetch_json(url, limit=2_000_000):
    if not allowed_url(url):
        raise ValueError("Untrusted release URL")
    req = request.Request(url, headers={"User-Agent": "ASN-Reservation-Update-Check", "Accept": "application/json"})
    with request.build_opener(SafeRedirect).open(req, timeout=10) as response:
        body = response.read(limit + 1)
        if len(body) > limit:
            raise ValueError("Release response too large")
        return json.loads(body)


def latest_policy(fetch=fetch_json):
    releases = []
    for page in range(1, 6):
        batch = fetch(f"https://api.github.com/repos/{REPOSITORY}/releases?per_page=100&page={page}")
        if not isinstance(batch, list):
            raise ValueError("Invalid release list")
        for item in batch:
            tag = item.get("tag_name", "")
            if item.get("draft") or item.get("prerelease") or not tag.startswith("server-v"):
                continue
            try:
                version(tag[8:])
            except ValueError:
                continue
            releases.append(item)
        if len(batch) < 100:
            break
    if not releases:
        raise ValueError("No server release")
    release = max(releases, key=lambda r: version(r["tag_name"][8:]))
    assets = [a for a in release.get("assets", []) if a.get("name") == "server-update-policy.json"]
    if len(assets) != 1:
        raise ValueError("Server release has no security policy yet")
    policy = fetch(assets[0]["browser_download_url"])
    if (not isinstance(policy, dict) or policy.get("schema") != 1 or policy.get("version") != release["tag_name"][8:] or
            version(policy.get("minimumSafeServerVersion")) > version(policy["version"])):
        raise ValueError("Invalid release policy")
    return policy


class ServerUpdates:
    def __init__(self, connect, current, log):
        self.connect, self.current, self.log = connect, current, log
        self.lock = threading.Lock()
        self.check_lock = threading.Lock()
        self.last_attempt = 0
        self.error = None
        with self.connect() as conn:
            conn.execute("""CREATE TABLE IF NOT EXISTS server_update_policy (
                id INTEGER PRIMARY KEY CHECK(id=1), minimum TEXT NOT NULL,
                latest TEXT NOT NULL, checked_at INTEGER NOT NULL)""")
            conn.execute("""CREATE TABLE IF NOT EXISTS security_policy (
                id INTEGER PRIMARY KEY CHECK(id=1), revision INTEGER NOT NULL,
                client_minimum TEXT NOT NULL, server_minimum TEXT NOT NULL, reason TEXT NOT NULL)""")

    def client_minimum(self):
        with self.connect() as conn:
            row = conn.execute("SELECT client_minimum FROM security_policy WHERE id=1").fetchone()
        return row[0] if row else "2.0.0"

    def check_security(self, fetch=fetch_json):
        try:
            policy = fetch_policy(fetch)
            with self.connect() as conn:
                conn.execute("BEGIN IMMEDIATE")
                old = conn.execute("SELECT revision,client_minimum,server_minimum FROM security_policy WHERE id=1").fetchone()
                if old and (policy["revision"] <= old[0] or version(policy["minimumClientVersion"]) < version(old[1]) or version(policy["minimumServerVersion"]) < version(old[2])):
                    return
                conn.execute("INSERT OR REPLACE INTO security_policy VALUES(1,?,?,?,?)", (policy["revision"], policy["minimumClientVersion"], policy["minimumServerVersion"], policy["reason"]))
                conn.commit()
            self.log("security_policy_checked", revision=policy["revision"])
        except Exception as exc:
            self.log("security_policy_check_failed", kind=type(exc).__name__)

    def status(self):
        with self.connect() as conn:
            row = conn.execute("SELECT minimum,latest,checked_at FROM server_update_policy WHERE id=1").fetchone()
        minimum, latest, checked = tuple(row) if row else (None, None, None)
        with self.connect() as conn:
            central = conn.execute("SELECT server_minimum,client_minimum,revision,reason FROM security_policy WHERE id=1").fetchone()
        if central:
            minimum = max(filter(None, (minimum, central[0])), key=version)
        blocked = bool(minimum and version(self.current) < version(minimum))
        with self.lock:
            error, attempted = self.error, self.last_attempt
        return {"currentVersion": self.current, "latestVersion": latest, "minimumSafeServerVersion": minimum,
                "minimumSafeClientVersion": central[1] if central else None,
                "securityPolicyRevision": central[2] if central else None,
                "securityReason": central[3] if central else None,
                "blocked": blocked, "updateAvailable": bool(latest and version(latest) > version(self.current)),
                "checkedAt": checked, "lastAttempt": attempted, "checkFailed": bool(error),
                "state": "blocked" if blocked else "unknown" if not row else "update_available" if version(latest) > version(self.current) else "current",
                "releaseUrl": f"https://github.com/{REPOSITORY}/releases/tag/server-v{latest}" if latest else None}

    def check(self, fetch=fetch_json):
        if not self.check_lock.acquire(blocking=False):
            return self.status()
        try:
            with self.lock:
                self.last_attempt = int(time.time())
            policy = latest_policy(fetch)
            with self.connect() as conn:
                conn.execute("BEGIN IMMEDIATE")
                old = conn.execute("SELECT minimum,latest FROM server_update_policy WHERE id=1").fetchone()
                # A stale response or rollback must never erase a known safety floor.
                minimum = max([policy["minimumSafeServerVersion"]] + ([old[0]] if old else []), key=version)
                latest = max([policy["version"]] + ([old[1]] if old else []), key=version)
                conn.execute("INSERT OR REPLACE INTO server_update_policy VALUES(1,?,?,?)", (minimum, latest, int(time.time())))
                conn.commit()
            with self.lock:
                self.error = None
            self.log("server_update_checked", latest=latest, minimum_safe=minimum)
        except Exception as exc:
            with self.lock:
                self.error = type(exc).__name__
            self.log("server_update_check_failed", kind=type(exc).__name__)
        finally:
            self.check_lock.release()
        return self.status()

    def refresh(self):
        self.check_security()
        return self.check()

    def start(self):
        def worker():
            while True:
                self.refresh()
                time.sleep(3600)
        threading.Thread(target=worker, name="server-update-check", daemon=True).start()
