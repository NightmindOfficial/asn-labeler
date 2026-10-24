"""Local Docker-host updater handshake. Not exposed as an HTTP endpoint."""
import argparse
import json
from pathlib import Path
import time
import secrets

import app
from server_updates import version


def prepare(target, lease=None):
    if version(target) <= version(app.VERSION):
        raise ValueError("Target must be newer than installed version")
    lease = lease if lease is not None else secrets.randbelow(2**62) + 1
    if type(lease) is not int or not 0 < lease < 2**63:
        raise ValueError("Invalid maintenance lease")
    with app.transaction() as conn:
        active = conn.execute("SELECT value FROM metadata WHERE key='update_maintenance_until'").fetchone()
        if active and active[0] > app.now():
            raise ValueError("Another maintenance lease is active")
        if conn.execute("SELECT 1 FROM reservations WHERE status='printing' LIMIT 1").fetchone():
            raise ValueError("Printing or unresolved print exists; update postponed")
        if conn.execute("SELECT 1 FROM secondary_labels WHERE printed_at IS NULL LIMIT 1").fetchone():
            raise ValueError("Unconfirmed secondary label exists; update postponed")
        conn.execute("INSERT OR REPLACE INTO metadata VALUES('update_maintenance_until',?)", (app.now() + 1800,))
        conn.execute("INSERT OR REPLACE INTO metadata VALUES('update_maintenance_owner',?)", (lease,))
    # The lease blocks reservations and print starts, but never acknowledgements.
    folder = Path(app.CONFIG["sqlite_path"]).parent / "backups"
    folder.mkdir(mode=0o700, exist_ok=True)
    destination = folder / ("before-update-" + str(time.time_ns()) + ".sqlite3")
    import sqlite3
    from contextlib import closing
    with closing(app.db_connect()) as source, closing(sqlite3.connect(destination)) as backup:
        source.backup(backup)
    destination.chmod(0o600)
    return {"ready": True, "backup": destination.name, "target": target, "lease": lease}


def resume(lease):
    with app.transaction() as conn:
        owner = conn.execute("SELECT value FROM metadata WHERE key='update_maintenance_owner'").fetchone()
        if not owner or owner[0] != lease:
            return {"resumed": False}
        conn.execute("DELETE FROM metadata WHERE key='update_maintenance_until'")
        conn.execute("DELETE FROM metadata WHERE key='update_maintenance_owner'")
    return {"resumed": True}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("prepare", "resume"))
    parser.add_argument("--target")
    parser.add_argument("--lease", type=int, required=True)
    args = parser.parse_args()
    try:
        print(json.dumps(prepare(args.target, args.lease) if args.action == "prepare" else resume(args.lease)))
    except Exception as exc:
        # Do not emit configuration, tokens or remote responses.
        print(json.dumps({"ready": False, "error": str(exc) if isinstance(exc, ValueError) else type(exc).__name__}))
        raise SystemExit(1)
