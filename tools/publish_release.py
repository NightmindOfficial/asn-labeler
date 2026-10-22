"""Publish verified main-branch artifacts, using a draft as the upload boundary."""
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError
from urllib.parse import quote, urlsplit
from urllib.request import Request, urlopen
from release_common import COMPONENTS, digest, read_version, release_notes
from verify_artifacts import verify

ROOT = Path(__file__).resolve().parents[1]


class GitHub:
    def __init__(self, repository, token):
        if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository):
            raise ValueError("Invalid repository")
        self.base = "https://api.github.com/repos/" + repository
        self.token = token

    def request(self, method, path, data=None, binary=None):
        url = path if path.startswith("https://") else self.base + path
        if urlsplit(url).netloc not in ("api.github.com", "uploads.github.com"):
            raise ValueError("Untrusted GitHub endpoint")
        headers = {"Authorization": "Bearer " + self.token,
                   "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"}
        body = binary if binary is not None else json.dumps(data).encode() if data is not None else None
        headers["Content-Type"] = "application/octet-stream" if binary is not None else "application/json"
        try:
            with urlopen(Request(url, data=body, headers=headers, method=method), timeout=180) as response:
                content = response.read()
                return json.loads(content) if content else None
        except HTTPError as exc:
            if method == "GET" and exc.code == 404:
                return None
            # Do not echo request headers, tokens or arbitrary response content.
            raise RuntimeError(f"GitHub {method} failed with HTTP {exc.code}") from None

    def tag_commit(self, tag):
        ref = self.request("GET", "/git/ref/tags/" + quote(tag, safe=""))
        if ref is None:
            return None
        obj = ref["object"]
        for _ in range(10):
            if obj["type"] == "commit":
                return obj["sha"]
            if obj["type"] != "tag":
                break
            obj = self.request("GET", "/git/tags/" + obj["sha"])["object"]
        raise ValueError("Release tag does not resolve to a commit")


def find_release(api, tag):
    # The tag endpoint only guarantees published releases; drafts need the list endpoint.
    existing = api.request("GET", "/releases/tags/" + tag)
    if existing:
        return existing
    for page in range(1, 101):
        releases = api.request("GET", f"/releases?per_page=100&page={page}")
        matches = [release for release in releases if release["tag_name"] == tag]
        if len(matches) > 1:
            raise ValueError("Multiple matching releases; inspect them before retrying")
        if matches:
            return matches[0]
        if len(releases) < 100:
            return None
    raise ValueError("Release lookup limit reached; no release was modified")


def publish(api, directory, version, commit, notes, names, component="client"):
    tag = component + "-v" + version
    existing = find_release(api, tag)
    if existing and not existing["draft"]:
        print(tag, "is already published; leaving it unchanged.")
        return "unchanged"
    # Avoid publishing an obsolete queued workflow after main has advanced.
    branch = api.request("GET", "/git/ref/heads/main")
    if branch["object"]["sha"] != commit:
        print("main advanced; this workflow will not publish an older snapshot.")
        return "superseded"
    tagged = api.tag_commit(tag)
    if tagged is not None and tagged != commit:
        raise ValueError("Release tag belongs to a different commit; use a new version")
    if existing and existing["target_commitish"] != commit:
        raise ValueError("Existing draft belongs to another commit; inspect it before retrying")
    release = existing or api.request("POST", "/releases", {
        "tag_name": tag, "target_commitish": commit, "name": "ASN Labeler " + component.title() + " " + version,
        "body": notes, "draft": True, "prerelease": False,
    })
    endpoint = "/releases/" + str(release["id"])
    old_assets = api.request("GET", endpoint + "/assets?per_page=100")
    if len(old_assets) >= 100 or any(a["name"] not in names for a in old_assets):
        raise ValueError("Draft contains unexpected assets; inspect it before retrying")
    # Only drafts can be replaced. A published release is never mutated.
    for asset in old_assets:
        api.request("DELETE", "/releases/assets/" + str(asset["id"]))
    upload = release["upload_url"].split("{", 1)[0]
    for name in names:
        api.request("POST", upload + "?name=" + quote(name, safe=""), binary=(Path(directory) / name).read_bytes())
    uploaded = api.request("GET", endpoint + "/assets?per_page=100")
    if sorted(a["name"] for a in uploaded) != sorted(names):
        raise ValueError("Incomplete release upload; draft remains unpublished")
    for asset in uploaded:
        if asset["state"] != "uploaded" or asset["size"] != (Path(directory) / asset["name"]).stat().st_size:
            raise ValueError("Incomplete asset; draft remains unpublished")
        if asset.get("digest") and asset["digest"] != "sha256:" + digest(Path(directory) / asset["name"]):
            raise ValueError("Uploaded asset digest mismatch; draft remains unpublished")
    # Check again after large uploads; do not publish if main changed in the meantime.
    if api.request("GET", "/git/ref/heads/main")["object"]["sha"] != commit:
        raise ValueError("main advanced during upload; draft remains unpublished")
    api.request("PATCH", endpoint, {"body": notes, "draft": False, "make_latest": "false"})
    print("Published", tag)
    return "published"


def main():
    if os.environ.get("GITHUB_REF") != "refs/heads/main" or os.environ.get("GITHUB_EVENT_NAME") not in ("push", "workflow_dispatch"):
        raise ValueError("Publishing is allowed only from a main-branch workflow")
    commit = os.environ.get("GITHUB_SHA", "")
    if not re.fullmatch(r"[0-9a-f]{40}", commit):
        raise ValueError("Invalid release commit")
    component, directory = sys.argv[1], Path(sys.argv[2])
    version = read_version(ROOT, component)
    names = verify(directory, version, commit, component)
    notes = release_notes((ROOT / COMPONENTS[component] / "CHANGELOG.md").read_text(encoding="utf-8"), version)
    api = GitHub(os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_TOKEN"])
    publish(api, directory, version, commit, notes, names, component)


if __name__ == "__main__":
    main()
