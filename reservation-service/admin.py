"""Local operator console. Uses authenticated API; never edits tables directly."""
import argparse
import json
import os
import sqlite3
import sys
import time
from contextlib import closing
from pathlib import Path
from urllib import request
from urllib.error import HTTPError


def call(path, body=None):
    keys = [x.strip() for x in os.environ.get("ADMIN_API_KEYS", "").split(",") if x.strip()]
    if not keys:
        raise RuntimeError("ADMIN_API_KEYS fehlt. Separaten Admin-Schluessel konfigurieren.")
    data = None if body is None else json.dumps(body).encode()
    req = request.Request("http://127.0.0.1:" + os.environ.get("PORT", "8765") + path,
                          data=data, headers={"Authorization": "Bearer " + keys[0], "Content-Type": "application/json"})
    try:
        with request.urlopen(req, timeout=30) as response:
            return json.load(response)
    except HTTPError as exc:
        error = json.load(exc)
        raise RuntimeError(f"HTTP {exc.code}: {error.get('error')} | Request-ID {error.get('requestId')}") from None


def backup():
    source = Path(os.environ.get("SQLITE_PATH", "/data/reservations.sqlite3"))
    if not source.is_file():
        raise RuntimeError("Datenbank fehlt; kein leeres Backup erzeugt.")
    folder = source.parent / "backups"
    folder.mkdir(exist_ok=True, mode=0o700)
    dest = folder / ("reservations-" + time.strftime("%Y%m%d-%H%M%S") + "-" + str(time.time_ns()) + ".sqlite3")
    # SQLite backup API includes committed WAL data while the service is running.
    with closing(sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True)) as conn:
        with closing(sqlite3.connect(dest)) as output:
            conn.backup(output)
            if output.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise RuntimeError("Backup integrity check failed")
    os.chmod(dest, 0o600)
    return {"backup": str(dest), "integrity": "ok"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["status", "events", "sync", "archive-open", "backup"])
    parser.add_argument("--after", type=int, default=0, help="Audit sequence cursor")
    parser.add_argument("--page", type=int, default=1, help="State page, 1000 rows each")
    args = parser.parse_args()
    if args.action == "backup":
        result = backup()
    elif args.action == "status":
        result = call("/api/admin/state?page=" + str(args.page))
    elif args.action == "events":
        result = call("/api/admin/events?after=" + str(max(0, args.after)))
    elif args.action == "sync":
        print("Nicht gestartete Reservierungen freigeben; gedruckte/unklare ASNs bleiben erhalten.")
        if input("Zum Fortfahren SYNC eingeben: ") != "SYNC":
            return
        result = call("/api/admin/reset-to-paperless", {})
    else:
        preview = call("/api/admin/archive-preview", {})
        print(json.dumps(preview, indent=2))
        print("Offene Eintraege werden dauerhaft GESPERRT, nicht geloescht. Keine Wiederverwendung.")
        if input("Alle Druck-Clients anhalten. Zum Fortfahren BLOCK OUTSTANDING eingeben: ") != "BLOCK OUTSTANDING":
            return
        if input("Wirklich dauerhaft sperren? YES eingeben: ") != "YES":
            return
        result = call("/api/admin/archive-open", {"previewToken": preview["previewToken"], "confirmation": "BLOCK OUTSTANDING"})
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, ValueError) as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(1)
