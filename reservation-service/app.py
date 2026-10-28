#!/usr/bin/env python3
"""ASN service: single SQLite authority, fail closed when Paperless is unavailable."""
import contextvars
import hashlib
import hmac
import json
import os
import re
import sqlite3
import threading
import time
import uuid
import tempfile
from server_updates import ServerUpdates
from pathlib import Path
from collections import OrderedDict, deque
from contextlib import contextmanager, closing
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from http import HTTPStatus
from urllib import request
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit, parse_qs

VERSION = Path(__file__).with_name("VERSION").read_text(encoding="utf-8").strip()
INSTANCE_ID = uuid.uuid4().hex
CLIENT_VERSION_FLOOR = "2.1.1"
RECENT_LOGS = deque(maxlen=300)
LOG_LOCK = threading.Lock()
STATIC_DIR = Path(__file__).with_name("web")
CONFIG = {
    "paperless_url": os.environ.get("PAPERLESS_URL", "").rstrip("/"),
    "paperless_token": os.environ.get("PAPERLESS_TOKEN", ""),
    "asn_prefix": os.environ.get("ASN_PREFIX", "ASN"),
    "asn_padding": int(os.environ.get("ASN_PADDING", "5")),
    "api_keys": {x.strip() for x in os.environ.get("API_KEYS", "").split(",") if x.strip()},
    "admin_keys": {x.strip() for x in os.environ.get("ADMIN_API_KEYS", "").split(",") if x.strip()},
    "sqlite_path": os.environ.get("SQLITE_PATH", "/data/reservations.sqlite3"),
    "listen_addr": os.environ.get("LISTEN_ADDR", "0.0.0.0"),
    "port": int(os.environ.get("PORT", "8765")),
    "reservation_ttl": int(os.environ.get("RESERVATION_TTL_SECONDS", "300")),
    "printing_settle": int(os.environ.get("PRINTING_SETTLE_SECONDS", "30")),
}
STATES = {"reserved", "printing", "printed", "reusable", "blocked"}
TRANSITIONS = {"printing": {"reserved"}, "printed": {"printing"}, "released": {"reserved"}}
REQUEST_ID = contextvars.ContextVar("request_id", default="local")
ACTOR = contextvars.ContextVar("actor", default="local")
OWNER = contextvars.ContextVar("owner", default="")
REQUEST_CLIENT_VERSION = contextvars.ContextVar("client_version", default="")


def version_parts(value):
    if not isinstance(value, str) or not re.fullmatch(r"(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})", value):
        raise ValueError("Invalid client version")
    parts = tuple(map(int, value.split(".")))
    if any(part > 65534 for part in parts):
        raise ValueError("Invalid client version")
    return parts


def minimum_client_version():
    return max(CLIENT_VERSION_FLOOR, server_updates().client_minimum(), key=version_parts)


class AdmissionBlocked(Exception):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def check_admission(conn):
    # Called under BEGIN IMMEDIATE: no new work can slip past a policy update
    # or a maintenance lease between checking and reserving/starting a job.
    lease = conn.execute("SELECT value FROM metadata WHERE key='update_maintenance_until'").fetchone()
    if lease and int(lease[0]) > now():
        raise AdmissionBlocked("service_maintenance")
    client = REQUEST_CLIENT_VERSION.get()
    floor = CLIENT_VERSION_FLOOR
    if conn.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='security_policy'").fetchone():
        central = conn.execute("SELECT server_minimum,client_minimum FROM security_policy WHERE id=1").fetchone()
        if central and version_parts(VERSION) < version_parts(central[0]):
            raise AdmissionBlocked("server_update_required")
        if central:
            floor = max(floor, central[1], key=version_parts)
    if client and version_parts(client) < version_parts(floor):
        raise AdmissionBlocked("client_update_required")
    if conn.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='server_update_policy'").fetchone():
        policy = conn.execute("SELECT minimum FROM server_update_policy WHERE id=1").fetchone()
        if policy and version_parts(VERSION) < version_parts(policy[0]):
            raise AdmissionBlocked("server_update_required")


UPDATES = None
UPDATES_LOCK = threading.Lock()


def server_updates():
    global UPDATES
    with UPDATES_LOCK:
        if UPDATES is None:
            UPDATES = ServerUpdates(lambda: closing(db_connect()), VERSION, log)
        return UPDATES


def completes_existing_job(method, path):
    # Finishing an owned job must remain possible after the minimum is raised.
    return method == "POST" and (path == "/api/documents/copy-labels/printed" or
        re.fullmatch(r"/api/reservations/[a-zA-Z0-9-]{1,64}/(printed|release)", path) is not None)


def now():
    return int(time.time())


def log(event, **fields):
    # Deliberately no headers, request bodies, URLs, document titles or exception text.
    entry = {"time": now(), "event": event, "request_id": REQUEST_ID.get(), **fields}
    with LOG_LOCK:
        RECENT_LOGS.append(entry)
    try:
        print(json.dumps(entry, ensure_ascii=True), flush=True)
    except OSError:
        pass  # Logging must not turn an already committed transaction into a failure.


def barcode_text(number):
    return f"{CONFIG['asn_prefix']}{number:0{CONFIG['asn_padding']}d}"


def audit(conn, row, target, reason):
    conn.execute("""INSERT INTO audit_events
        (time, request_id, actor, reservation_id, asn_number, old_status, new_status, reason)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
        (now(), REQUEST_ID.get(), ACTOR.get(), row["id"], row["asn_number"], row["status"], target, reason))


def db_connect():
    os.makedirs(os.path.dirname(CONFIG["sqlite_path"]) or ".", exist_ok=True)
    conn = sqlite3.connect(CONFIG["sqlite_path"], timeout=10, isolation_level=None)
    conn.row_factory = sqlite3.Row
    try:
        conn.execute("PRAGMA journal_mode=WAL")
        conn.execute("PRAGMA synchronous=FULL")
        if conn.execute("PRAGMA user_version").fetchone()[0] > 3:
            raise RuntimeError("Database schema is newer than this service; downgrade refused")
        if conn.execute("PRAGMA user_version").fetchone()[0] < 2:
            conn.execute("BEGIN IMMEDIATE")
            if conn.execute("PRAGMA user_version").fetchone()[0] >= 2:
                conn.commit()
                conn.close()
                return db_connect()
            conn.execute("""CREATE TABLE IF NOT EXISTS reservations (
                id TEXT PRIMARY KEY, asn_number INTEGER NOT NULL CHECK(asn_number > 0),
                barcode_text TEXT NOT NULL,
                status TEXT NOT NULL CHECK(status IN ('reserved','printing','printed','reusable','blocked')),
                created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL, expires_at INTEGER NOT NULL,
                owner TEXT NOT NULL DEFAULT '')""")
            duplicates = conn.execute("SELECT asn_number FROM reservations GROUP BY asn_number HAVING COUNT(*) > 1 LIMIT 1").fetchone()
            if duplicates:
                raise RuntimeError("Migration stopped: duplicate ASN rows. Back up database and inspect; no automatic deletion.")
            invalid = conn.execute("""SELECT 1 FROM reservations WHERE typeof(asn_number) != 'integer'
                OR asn_number < 1 OR asn_number > 2147483647 LIMIT 1""").fetchone()
            if invalid:
                raise RuntimeError("Migration stopped: invalid ASN values. Back up database and inspect.")
            if "owner" not in {r["name"] for r in conn.execute("PRAGMA table_info(reservations)")}:
                conn.execute("ALTER TABLE reservations ADD COLUMN owner TEXT NOT NULL DEFAULT ''")
            conn.execute("""CREATE TABLE IF NOT EXISTS audit_events (
                seq INTEGER PRIMARY KEY AUTOINCREMENT, time INTEGER NOT NULL,
                request_id TEXT NOT NULL, actor TEXT NOT NULL, reservation_id TEXT NOT NULL,
                asn_number INTEGER NOT NULL, old_status TEXT NOT NULL, new_status TEXT NOT NULL,
                reason TEXT NOT NULL)""")
            conn.execute("CREATE TABLE IF NOT EXISTS metadata (key TEXT PRIMARY KEY, value INTEGER NOT NULL)")
            conn.execute("INSERT OR IGNORE INTO metadata VALUES ('paperless_high_water', 0)")
            conn.execute("""CREATE TABLE IF NOT EXISTS admin_previews (
                token_hash TEXT PRIMARY KEY, revision INTEGER NOT NULL, expires_at INTEGER NOT NULL,
                candidates_hash TEXT NOT NULL)""")
            # Preserve old IDs/history. Old 'released' could only be reached from reserved.
            for row in conn.execute("SELECT * FROM reservations").fetchall():
                state = row["status"]
                target = "reusable" if state == "released" else state if state in STATES else "blocked"
                audit(conn, row, target, "migration_baseline")
                conn.execute("UPDATE reservations SET status=? WHERE id=?", (target, row["id"]))
            conn.execute("UPDATE reservations SET expires_at=? WHERE status='reserved' AND expires_at=0",
                         (now() + CONFIG["reservation_ttl"],))
            conn.execute("CREATE UNIQUE INDEX IF NOT EXISTS uq_reservations_asn ON reservations(asn_number)")
            conn.execute("CREATE INDEX IF NOT EXISTS idx_reservations_status ON reservations(status)")
            # Legacy tables do not have the new CHECK constraints.
            for operation in ("INSERT", "UPDATE"):
                conn.execute(f"""CREATE TRIGGER IF NOT EXISTS valid_reservation_{operation.lower()}
                    BEFORE {operation} ON reservations
                    WHEN typeof(NEW.asn_number) != 'integer' OR NEW.asn_number <= 0 OR NEW.asn_number > 2147483647
                    OR NEW.status NOT IN ('reserved','printing','printed','reusable','blocked')
                    BEGIN SELECT RAISE(ABORT, 'invalid reservation state'); END""")
            conn.execute("PRAGMA user_version=2")
            conn.execute("COMMIT")
        if conn.execute("PRAGMA user_version").fetchone()[0] < 3:
            conn.execute("BEGIN IMMEDIATE")
            conn.execute("""CREATE TABLE IF NOT EXISTS secondary_labels (
                id TEXT PRIMARY KEY, document_id INTEGER NOT NULL CHECK(document_id > 0),
                asn_number INTEGER NOT NULL CHECK(asn_number > 0), owner TEXT NOT NULL,
                created_at INTEGER NOT NULL, printed_at INTEGER,
                CHECK(printed_at IS NULL OR printed_at >= 0))""")
            conn.execute("CREATE INDEX IF NOT EXISTS idx_secondary_document ON secondary_labels(document_id, asn_number)")
            conn.execute("PRAGMA user_version=3")
            conn.commit()
        return conn
    except Exception:
        if conn.in_transaction:
            conn.rollback()
        conn.close()
        raise


@contextmanager
def transaction():
    conn = db_connect()
    try:
        conn.execute("BEGIN IMMEDIATE")
        before = conn.execute("SELECT COALESCE(MAX(seq),0) FROM audit_events").fetchone()[0]
        yield conn
        events = [dict(r) for r in conn.execute("SELECT * FROM audit_events WHERE seq > ? ORDER BY seq", (before,))]
        conn.commit()
        for event in events:
            log("state_changed", **event)
    except Exception:
        if conn.in_transaction:
            conn.rollback()
        raise
    finally:
        conn.close()


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class PaperlessError(RuntimeError):
    def __init__(self, code, fields=None):
        self.code = code
        self.fields = fields or []
        super().__init__(code)


def paperless_hint(code):
    return {
        "paperless_http_400": "Paperless lehnt die Anfrage ab. Filterfelder im Diagnoseprotokoll pruefen; bei HTML-Fehlern auch PAPERLESS_URL und Allowed Hosts pruefen.",
        "paperless_http_401": "Paperless-Token fehlt oder ist ungueltig. PAPERLESS_TOKEN pruefen.",
        "paperless_http_403": "Paperless verweigert den Zugriff. Token und Dokumentberechtigungen pruefen.",
        "paperless_http_404": "Paperless-API nicht gefunden. PAPERLESS_URL muss die Basisadresse sein, ohne /api/documents/.",
        "paperless_unreachable": "Paperless aus dem Container nicht erreichbar. PAPERLESS_URL, Netzwerk und DNS pruefen.",
    }.get(code, "Paperless-Verbindung und Service-Protokoll pruefen. Keine Nummer wurde automatisch freigegeben.")


def paperless_documents(params):
    if not CONFIG["paperless_url"] or not CONFIG["paperless_token"]:
        raise PaperlessError("paperless_configuration_missing")
    url = f"{CONFIG['paperless_url']}/api/documents/?{urlencode(params)}"
    req = request.Request(url, headers={"Authorization": f"Token {CONFIG['paperless_token']}"})
    started = time.monotonic()
    # Only our fixed filter names are logged, never response contents or credentials.
    safe_params = {k: v for k, v in params.items() if k in {
        "ordering", "page_size", "archive_serial_number", "archive_serial_number__gt", "archive_serial_number__lte"}}
    log("paperless_request", message="Paperless-Dokumente abfragen", endpoint="documents", filters=safe_params)
    try:
        # Never forward the Paperless token to a redirect destination.
        with request.build_opener(NoRedirect).open(req, timeout=8) as resp:
            raw = resp.read(4 * 1024 * 1024 + 1)
            if len(raw) > 4 * 1024 * 1024:
                raise PaperlessError("paperless_response_too_large")
            data = json.loads(raw)
    except HTTPError as exc:
        fields = []
        try:
            details = json.loads(exc.read(16384))
            if isinstance(details, dict):
                fields = sorted(k for k in details if k in set(safe_params) | {"archive_serial_number__isnull", "detail", "non_field_errors"})
        except (ValueError, OSError):
            pass
        finally:
            exc.close()
        code = "paperless_http_" + str(exc.code)
        log("paperless_failed", http_status=exc.code, validation_fields=fields,
            hint=paperless_hint(code), duration_ms=round((time.monotonic() - started) * 1000))
        raise PaperlessError(code, fields) from exc
    except (URLError, TimeoutError) as exc:
        log("paperless_failed", error_code="paperless_unreachable", hint=paperless_hint("paperless_unreachable"))
        raise PaperlessError("paperless_unreachable") from exc
    except ValueError as exc:
        raise PaperlessError("paperless_response_invalid") from exc
    if not isinstance(data, dict) or not isinstance(data.get("results"), list):
        raise PaperlessError("paperless_response_invalid")
    log("paperless_response", message="Paperless-Antwort erhalten", http_status=200, results=len(data["results"]),
        duration_ms=round((time.monotonic() - started) * 1000))
    return data


def paperless_max_asn():
    results = paperless_documents({"ordering": "-archive_serial_number", "page_size": "1",
                                  "archive_serial_number__gt": "0"})["results"]
    if not results:
        return 0
    value = results[0].get("archive_serial_number")
    if type(value) is not int or value < 1:
        raise PaperlessError("paperless_asn_invalid")
    return value


def remote_floor(conn, remote_max):
    if type(remote_max) is not int or remote_max < 0:
        raise RuntimeError("Invalid Paperless maximum")
    conn.execute("UPDATE metadata SET value=MAX(value,?) WHERE key='paperless_high_water'", (remote_max,))
    return conn.execute("SELECT value FROM metadata WHERE key='paperless_high_water'").fetchone()[0]


def set_state(conn, row, state, reason):
    audit(conn, row, state, reason)
    conn.execute("UPDATE reservations SET status=?, updated_at=?, expires_at=0 WHERE id=?",
                 (state, now(), row["id"]))


def free_state(conn, number, floor):
    higher = conn.execute("""SELECT 1 FROM reservations WHERE asn_number > ?
        AND status IN ('reserved','printing','printed') LIMIT 1""", (number,)).fetchone()
    return "blocked" if number <= floor or higher else "reusable"


def expire_reserved(conn, floor):
    # Highest first: a crashed multi-reservation tail can safely unwind.
    for row in conn.execute("""SELECT * FROM reservations WHERE status='reserved'
        AND expires_at > 0 AND expires_at <= ? ORDER BY asn_number DESC""", (now(),)).fetchall():
        set_state(conn, row, free_state(conn, row["asn_number"], floor), "reservation_expired")


def next_number(conn, remote_max):
    floor = max(remote_max, conn.execute("SELECT value FROM metadata WHERE key='paperless_high_water'").fetchone()[0])
    return allocation_number(conn, floor)


def allocation_number(conn, floor):
    # A block protects its own number, not lower explicitly released numbers.
    active = conn.execute("SELECT COALESCE(MAX(asn_number),0) FROM reservations WHERE status IN ('reserved','printing','printed')").fetchone()[0]
    reusable = conn.execute("SELECT MIN(asn_number) FROM reservations WHERE status='reusable' AND asn_number > ?",
                            (max(floor, active),)).fetchone()[0]
    if reusable is not None:
        return reusable
    local = conn.execute("SELECT MAX(asn_number) FROM reservations WHERE status != 'reusable'").fetchone()[0] or 0
    number = max(floor, local) + 1
    if number > 2147483647:
        raise ValueError("ASN range exhausted")
    return number


def reserve_next():
    with transaction() as conn:
        check_admission(conn)
        floor = remote_floor(conn, paperless_max_asn())
        expire_reserved(conn, floor)
        number = next_number(conn, floor)
        old = conn.execute("SELECT * FROM reservations WHERE asn_number=?", (number,)).fetchone()
        if old:
            if old["status"] != "reusable":
                raise RuntimeError("Reservation invariant violated")
            audit(conn, old, "reusable", "replaced_by_new_reservation")
            conn.execute("DELETE FROM reservations WHERE id=?", (old["id"],))
        rid, current = str(uuid.uuid4()), now()
        expiry = current + CONFIG["reservation_ttl"]
        conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)",
                     (rid, number, barcode_text(number), "reserved", current, current, expiry, OWNER.get()))
        audit(conn, {"id": rid, "asn_number": number, "status": ""}, "reserved", "allocated")
    return {"reservationId": rid, "asnNumber": number, "barcodeText": barcode_text(number), "expiresAt": expiry}


def peek_next():
    with transaction() as conn:
        floor = remote_floor(conn, paperless_max_asn())
        expire_reserved(conn, floor)
        number = next_number(conn, floor)
    return {"asnNumber": number, "barcodeText": barcode_text(number)}


def transition(reservation_id, target):
    if target not in TRANSITIONS:
        raise ValueError("Unknown transition")
    with transaction() as conn:
        row = conn.execute("SELECT * FROM reservations WHERE id=?", (reservation_id,)).fetchone()
        if not row:
            raise KeyError("Reservation not found")
        if row["owner"] and OWNER.get() != row["owner"]:
            raise PermissionError("Reservation belongs to another client key")
        # Only the final acknowledgement may be retried. Repeating printing could
        # cause a caller to physically submit the same job twice.
        if target == "printed" and row["status"] == "printed":
            return {"ok": True, "reservationId": reservation_id, "status": "printed"}
        if row["status"] not in TRANSITIONS[target]:
            raise ValueError("Reservation changed; refresh status")
        if target == "printing":
            check_admission(conn)
            if row["expires_at"] <= now():
                raise ValueError("Reservation expired; do not print")
            state = "printing"
        elif target == "released":
            floor = remote_floor(conn, paperless_max_asn())
            state = free_state(conn, row["asn_number"], floor)
        else:
            state = "printed"
        set_state(conn, row, state, "client_" + target)
    return {"ok": True, "reservationId": reservation_id, "status": state}


def printing_in_progress(row):
    return row["status"] == "printing" and now() - row["updated_at"] < CONFIG["printing_settle"]


def resolve_uncertain(reservation_id, exists, cancel=False, confirm_block=False, physical_label_absent=False):
    if not exists and physical_label_absent is not True:
        raise ValueError("Confirm physicalLabelAbsent=true only after removing/destroying the label and stopping the printing client")
    with transaction() as conn:
        row = conn.execute("SELECT * FROM reservations WHERE id=?", (reservation_id,)).fetchone()
        if not row:
            raise KeyError("Reservation not found")
        if row["status"] != ("printed" if cancel else "printing"):
            raise ValueError("Reservation changed; refresh status")
        if printing_in_progress(row):
            raise ValueError(f"Print may still be running; stop the client and wait at least {CONFIG['printing_settle']} seconds")
        number = row["asn_number"]
        found = paperless_documents({"archive_serial_number": number, "page_size": 1})["results"]
        if any(doc.get("archive_serial_number") != number for doc in found):
            raise RuntimeError("Paperless ignored the requested ASN")
        floor = remote_floor(conn, paperless_max_asn())
        if found or exists:
            state = "printed"
        else:
            state = free_state(conn, number, floor)
            if state == "blocked" and number > floor and not confirm_block:
                return {"status": "confirmation_required"}
        set_state(conn, row, state, "label_exists" if exists else "label_destroyed_confirmed")
    return {"status": "imported" if found else "pending" if exists else "blocked" if state == "blocked" else "next"}


def reset_to_paperless():
    # Compatible endpoint, intentionally safer semantics: never discard evidence.
    with transaction() as conn:
        remote = paperless_max_asn()
        floor = remote_floor(conn, remote)
        changed = 0
        released = 0
        for row in conn.execute("SELECT * FROM reservations WHERE status='reserved' ORDER BY asn_number DESC").fetchall():
            state = free_state(conn, row["asn_number"], floor)
            set_state(conn, row, state, "safe_sync")
            released += state == "reusable"
            changed += 1
        retained = conn.execute("SELECT COUNT(*) FROM reservations WHERE asn_number > ? AND status IN ('printing','printed','blocked')", (remote,)).fetchone()[0]
        number = next_number(conn, floor)
    return {"ok": True, "paperlessMax": remote, "deletedReservations": 0,
            "releasedReservations": released, "invalidatedReservations": changed, "retainedReservations": retained,
            "nextAsnNumber": number, "nextBarcodeText": barcode_text(number), "safeSync": True}


def admin_state(page=1, refresh_imports=False, hide_printed=False):
    if page < 1:
        raise ValueError("page must be positive")
    sync_error = None
    if refresh_imports:
        with closing(db_connect()) as source:
            blocked = [dict(row) for row in source.execute("SELECT * FROM reservations WHERE status='blocked'")]
        try:
            reconcile_blocked_imports(blocked, paperless_for_rows(blocked))
        except PaperlessError as exc:
            # Keep local administration available when Paperless is offline.
            sync_error = exc.code
    conn = db_connect()
    try:
        rows = [dict(row) for row in conn.execute("""SELECT id, asn_number, barcode_text, status,
                created_at, updated_at, expires_at FROM reservations WHERE (?=0 OR status!='printed')
                ORDER BY asn_number DESC LIMIT 1000 OFFSET ?""",
                (int(hide_printed), (page - 1) * 1000))]
        for row in rows:
            row["displayStatus"] = ("in_print" if printing_in_progress(row) else "uncertain") if row["status"] == "printing" else row["status"]
        return {"version": VERSION, "items": rows, "page": page, "pageSize": 1000, "paperlessSyncError": sync_error,
                "total": conn.execute("SELECT COUNT(*) FROM reservations WHERE (?=0 OR status!='printed')", (int(hide_printed),)).fetchone()[0],
                "paperlessHighWater": conn.execute("SELECT value FROM metadata WHERE key='paperless_high_water'").fetchone()[0],
                "revision": conn.execute("SELECT COALESCE(MAX(seq),0) FROM audit_events").fetchone()[0]}
    finally:
        conn.close()


def admin_events(after=0):
    conn = db_connect()
    try:
        return {"items": [dict(row) for row in conn.execute("SELECT * FROM audit_events WHERE seq > ? ORDER BY seq LIMIT 200", (after,))]}
    finally:
        conn.close()


def recent_admin_events(before=None):
    if before is not None and not 1 <= before <= 9223372036854775807:
        raise ValueError("before must be positive")
    with closing(db_connect()) as conn:
        where, parameters = ("", ()) if before is None else ("WHERE seq < ?", (before,))
        rows = [dict(row) for row in conn.execute(
            f"SELECT * FROM audit_events {where} ORDER BY seq DESC LIMIT 21", parameters)]
    items = rows[:20]
    return {"items": items, "hasMore": len(rows) > 20,
            "nextBefore": items[-1]["seq"] if len(rows) > 20 else None}


def backup_bytes():
    # A consistent snapshot includes committed WAL records, not a raw file copy.
    with tempfile.TemporaryDirectory(prefix="asn-backup-") as folder:
        target = Path(folder) / "reservations.sqlite3"
        with closing(db_connect()) as source, closing(sqlite3.connect(target)) as dest:
            source.backup(dest)
            if dest.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise RuntimeError("Backup integrity check failed")
        if target.stat().st_size > 64 * 1024 * 1024:
            raise ValueError("Backup exceeds web download limit; use local admin backup")
        data = target.read_bytes()
    log("backup_created", actor=ACTOR.get(), bytes=len(data))
    return data


def backup_before_migration():
    source = Path(CONFIG["sqlite_path"])
    if not source.is_file():
        return
    with closing(sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True)) as conn:
        if conn.execute("PRAGMA user_version").fetchone()[0] >= 3:
            return
        folder = source.parent / "backups"
        folder.mkdir(exist_ok=True, mode=0o700)
        target = folder / ("pre-migration-" + uuid.uuid4().hex + ".sqlite3")
        with closing(sqlite3.connect(target)) as dest:
            conn.backup(dest)
            if dest.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise RuntimeError("Pre-migration backup failed integrity check")
        os.chmod(target, 0o600)
        log("migration_backup_created", file=target.name, message="Sicherung vor Schema-Update unter /data/backups erstellt.")


def paperless_for_rows(rows):
    documents = {}
    if rows:
        minimum, maximum = min(r["asn_number"] for r in rows), max(r["asn_number"] for r in rows)
        # Keyset pagination stays on the configured host and handles out-of-order imports.
        after = minimum - 1
        while after < maximum:
            data = paperless_documents({"archive_serial_number__gt": after,
                "archive_serial_number__lte": maximum, "ordering": "archive_serial_number", "page_size": 100})
            results = data["results"]
            if not results:
                break
            for doc in results:
                asn = doc.get("archive_serial_number")
                if type(asn) is not int or not after < asn <= maximum:
                    raise RuntimeError("Paperless ignored the requested ASN range")
                documents[asn] = doc
            after = max(doc["archive_serial_number"] for doc in results)
            if not data.get("next"):
                break
    return documents


def reconcile_blocked_imports(rows, documents):
    matches = [row for row in rows if row["status"] == "blocked"
               and row["asn_number"] in documents
               and documents[row["asn_number"]].get("archive_serial_number") == row["asn_number"]]
    if not matches:
        return
    # Remote I/O completed before the write lock. Recheck identity and state so
    # concurrent unblock/reallocation cannot update a different reservation.
    with transaction() as conn:
        for snapshot in matches:
            row = conn.execute("SELECT * FROM reservations WHERE id=? AND asn_number=? AND status='blocked'",
                               (snapshot["id"], snapshot["asn_number"])).fetchone()
            remote_floor(conn, snapshot["asn_number"])
            if row is not None:
                set_state(conn, row, "printed", "paperless_import_confirmed")
                snapshot["status"] = "printed"
                snapshot["updated_at"] = now()


def outstanding_rows(conn):
    rows = conn.execute("SELECT * FROM reservations WHERE status IN ('reserved','printing','printed')").fetchall()
    documents = paperless_for_rows(rows)
    return [row for row in rows if row['asn_number'] not in documents]


def candidate_hash(rows):
    return hashlib.sha256(json.dumps(sorted(row["id"] for row in rows)).encode()).hexdigest()


def high_water_snapshot(conn, remote):
    stored = conn.execute("SELECT value FROM metadata WHERE key='paperless_high_water'").fetchone()[0]
    return {"stored": stored, "remote": remote, "owner": OWNER.get()}


def high_water_preview():
    with transaction() as conn:
        remote = paperless_max_asn()
        snapshot = high_water_snapshot(conn, remote)
        if remote >= snapshot["stored"]:
            return {"needsCorrection": False, "storedHighWater": snapshot["stored"], "paperlessMax": remote}
        revision = conn.execute("SELECT COALESCE(MAX(seq),0) FROM audit_events").fetchone()[0]
        next_asn = allocation_number(conn, remote)
        protected = conn.execute("SELECT COUNT(*) FROM reservations WHERE asn_number > ? AND status != 'reusable'", (remote,)).fetchone()[0]
        token = uuid.uuid4().hex + uuid.uuid4().hex
        conn.execute("DELETE FROM admin_previews WHERE expires_at <= ?", (now(),))
        conn.execute("INSERT INTO admin_previews VALUES (?,?,?,?)", (hashlib.sha256(token.encode()).hexdigest(),
            revision, now() + 120, "high-water:" + json.dumps(snapshot, sort_keys=True)))
    return {"needsCorrection": True, "previewToken": token, "storedHighWater": snapshot["stored"], "paperlessMax": remote,
            "nextAsnNumber": next_asn, "protectedReservations": protected, "expiresIn": 120}


def correct_high_water(token, confirmation, physical_labels_absent):
    if confirmation != "LOWER HIGH WATER" or physical_labels_absent is not True:
        raise ValueError("Explicit confirmation and physicalLabelsAbsent=true required")
    with transaction() as conn:
        token_hash = hashlib.sha256(token.encode()).hexdigest()
        preview = conn.execute("SELECT * FROM admin_previews WHERE token_hash=?", (token_hash,)).fetchone()
        revision = conn.execute("SELECT COALESCE(MAX(seq),0) FROM audit_events").fetchone()[0]
        if not preview or preview["expires_at"] <= now() or preview["revision"] != revision:
            raise ValueError("Preview expired or state changed; preview again")
        remote = paperless_max_asn()
        snapshot = high_water_snapshot(conn, remote)
        if preview["candidates_hash"] != "high-water:" + json.dumps(snapshot, sort_keys=True):
            raise ValueError("Preview changed in Paperless, local high-water mark or administrator; preview again")
        if remote >= snapshot["stored"]:
            raise ValueError("Preview no longer lowers the high-water mark")
        conn.execute("UPDATE metadata SET value=? WHERE key='paperless_high_water'", (remote,))
        audit(conn, {"id": "paperless-high-water", "asn_number": remote,
            "status": "Hoechststand " + str(snapshot["stored"])}, "Hoechststand " + str(remote), "admin_high_water_corrected")
        conn.execute("DELETE FROM admin_previews WHERE token_hash=?", (token_hash,))
        number = next_number(conn, remote)
    return {"ok": True, "previousHighWater": snapshot["stored"], "paperlessHighWater": remote,
            "nextAsnNumber": number, "nextBarcodeText": barcode_text(number)}


def unblock_reservation(rid, confirmation, physical_label_absent):
    if confirmation != "UNBLOCK ASN" or physical_label_absent is not True:
        raise ValueError("Explicit confirmation and physicalLabelAbsent=true required")
    with transaction() as conn:
        row = conn.execute("SELECT * FROM reservations WHERE id=?", (rid,)).fetchone()
        if not row:
            raise KeyError(rid)
        if row["status"] != "blocked":
            raise ValueError("Reservation ist nicht mehr gesperrt. Ansicht aktualisieren.")
        # Keep the historical Paperless floor: lowering it is a separate audited action.
        remote = paperless_max_asn()
        floor = remote_floor(conn, remote)
        if row["asn_number"] <= floor:
            raise ValueError(f"Reservation ASN {row['asn_number']} liegt im geschuetzten Paperless-Bereich: aktuell {remote}, gespeicherter Hoechststand {floor}. Gegebenenfalls zuerst in der Danger Zone den Hoechststand korrigieren.")
        if conn.execute("SELECT 1 FROM reservations WHERE asn_number > ? AND status IN ('reserved','printing','printed') LIMIT 1",
                        (row["asn_number"],)).fetchone():
            raise ValueError("Reservation hat hoehere aktive ASNs (reserviert, Druck unklar oder gedruckt).")
        set_state(conn, row, "reusable", "admin_unblocked")
        number = next_number(conn, floor)
    return {"ok": True, "status": "reusable", "nextAsnNumber": number, "nextBarcodeText": barcode_text(number)}


def archive_preview():
    with transaction() as conn:
        remote = paperless_max_asn()
        remote_floor(conn, remote)
        rows = outstanding_rows(conn)
        revision = conn.execute("SELECT COALESCE(MAX(seq),0) FROM audit_events").fetchone()[0]
        token = uuid.uuid4().hex + uuid.uuid4().hex
        conn.execute("DELETE FROM admin_previews WHERE expires_at <= ?", (now(),))
        conn.execute("INSERT INTO admin_previews VALUES (?,?,?,?)",
            (hashlib.sha256(token.encode()).hexdigest(), revision, now() + 120, candidate_hash(rows)))
    return {"previewToken": token, "count": len(rows), "paperlessMax": remote,
            "action": "Block all outstanding ASNs not imported in Paperless; never reuse them", "expiresIn": 120}


def archive_open(token, confirmation):
    if confirmation != "BLOCK OUTSTANDING":
        raise ValueError("Explicit confirmation BLOCK OUTSTANDING required")
    with transaction() as conn:
        preview = conn.execute("SELECT * FROM admin_previews WHERE token_hash=?", (hashlib.sha256(token.encode()).hexdigest(),)).fetchone()
        revision = conn.execute("SELECT COALESCE(MAX(seq),0) FROM audit_events").fetchone()[0]
        if not preview or preview["expires_at"] <= now() or preview["revision"] != revision:
            raise ValueError("Preview expired or state changed; preview again")
        remote = paperless_max_asn()
        remote_floor(conn, remote)
        rows = outstanding_rows(conn)
        if candidate_hash(rows) != preview["candidates_hash"]:
            raise ValueError("Preview changed in Paperless; preview again")
        for row in rows:
            set_state(conn, row, "blocked", "admin_archive_open")
        conn.execute("DELETE FROM admin_previews WHERE token_hash=?", (preview["token_hash"],))
    return {"ok": True, "blocked": len(rows)}


def secondary_counts(documents):
    ids = sorted({doc.get("id") for doc in documents if type(doc.get("id")) is int})
    if not ids:
        return {}
    with closing(db_connect()) as conn:
        result = {}
        for offset in range(0, len(ids), 500):
            batch = ids[offset:offset + 500]
            for row in conn.execute("SELECT document_id, asn_number, COUNT(*) FROM secondary_labels "
                    "WHERE printed_at IS NOT NULL AND document_id IN (" + ",".join("?" for _ in batch) +
                    ") GROUP BY document_id, asn_number", batch):
                result[(row[0], row[1])] = row[2]
        return result


def secondary_label(body, confirm=False):
    identifier = body.get("printId")
    if not isinstance(identifier, str) or not re.fullmatch(r"[a-f0-9]{32}", identifier):
        raise ValueError("Invalid secondary print ID")
    with transaction() as conn:
        existing = conn.execute("SELECT * FROM secondary_labels WHERE id=?", (identifier,)).fetchone()
        if existing and existing["owner"] != OWNER.get():
            raise PermissionError()
        if confirm:
            if not existing:
                raise KeyError(identifier)
            if existing["printed_at"] is None:
                conn.execute("UPDATE secondary_labels SET printed_at=? WHERE id=?", (now(), identifier))
                audit(conn, {"id": identifier, "asn_number": existing["asn_number"], "status": "copy_prepared"},
                      "copy_printed", "secondary_label_confirmed")
        else:
            number, document_id = body.get("asnNumber"), body.get("documentId")
            check_admission(conn)
            if type(number) is not int or type(document_id) is not int or not 1 <= number <= 2147483647 or document_id < 1:
                raise ValueError("Invalid document identity")
            if existing:
                if (existing["document_id"], existing["asn_number"]) != (document_id, number):
                    raise ValueError("Secondary print ID already belongs to another document")
            else:
                docs = paperless_documents({"archive_serial_number": number, "page_size": 2})["results"]
                if len(docs) != 1 or docs[0].get("id") != document_id or docs[0].get("archive_serial_number") != number:
                    raise ValueError("Document or ASN changed in Paperless")
                conn.execute("INSERT INTO secondary_labels VALUES (?,?,?,?,?,NULL)",
                             (identifier, document_id, number, OWNER.get(), now()))
    return {"ok": True, "printId": identifier}


def copy_label_documents(page=1, asn=None):
    if not 1 <= page <= 2147483647 or (asn is not None and not 1 <= asn <= 2147483647):
        raise ValueError("invalid page or ASN")
    params = {"ordering": "-archive_serial_number", "page_size": 10, "page": page,
              "archive_serial_number__gt": 0}
    if asn is not None:
        params = {"archive_serial_number": asn, "page_size": 2}
    data = paperless_documents(params)
    copies = secondary_counts(data["results"])
    items = []
    seen = set()
    for doc in data["results"]:
        number, identifier = doc.get("archive_serial_number"), doc.get("id")
        if (type(number) is not int or not 1 <= number <= 2147483647 or
                type(identifier) is not int or identifier < 1 or number in seen or
                (asn is not None and number != asn)):
            raise PaperlessError("paperless_response_invalid")
        seen.add(number)
        items.append({"documentId": identifier, "asnNumber": number,
                      "barcodeText": barcode_text(number), "documentTitle": doc.get("title", ""),
                      "status": "imported", "secondaryLabelCount": copies.get((identifier, number), 0)})
    if asn is not None and not items:
        raise ValueError("Dokument mit dieser ASN nicht mehr in Paperless vorhanden.")
    return {"items": sorted(items, key=lambda item: -item["asnNumber"]),
            "hasMore": bool(data.get("next")) if asn is None else False}


def document_status(page: int = 1, include_completed: bool = True, hide_imported: bool = False) -> dict:
    if page < 1:
        raise ValueError("page must be positive")
    remote_max = paperless_max_asn()
    with transaction() as conn:
        floor = remote_floor(conn, remote_max)
        expire_reserved(conn, floor)
        rows = [dict(row) for row in conn.execute("SELECT * FROM reservations WHERE status != 'reusable' ORDER BY asn_number DESC")]
    documents = paperless_for_rows(rows)
    copies = secondary_counts(documents.values())
    reconcile_blocked_imports(rows, documents)
    counts = {"pending": 0, "in_print": 0, "uncertain": 0, "imported": 0, "unused": 0, "blocked": 0}
    items = []
    for row in rows:
        doc = documents.get(row["asn_number"])
        state = "imported" if doc else {"printed": "pending", "printing": "uncertain", "blocked": "blocked"}.get(row["status"], "unused")
        if not doc and printing_in_progress(row):
            state = "in_print"
        counts[state] += 1
        items.append({"reservationId": row["id"], "asnNumber": row["asn_number"], "barcodeText": row["barcode_text"],
            "status": state, "updatedAt": row["updated_at"], "documentTitle": doc.get("title", "") if doc else "",
            "secondaryLabelCount": copies.get((doc.get("id"), row["asn_number"]), 0) if doc else 0})
    conn = db_connect()
    try:
        next_asn = next_number(conn, remote_max)
    finally:
        conn.close()
    items.sort(key=lambda item: -item["asnNumber"])
    if not include_completed:
        items = [item for item in items if item["status"] not in ("imported", "blocked")]
    if hide_imported:
        items = [item for item in items if item["status"] != "imported"]
    return {"items": items[(page - 1) * 10:page * 10], "total": len(items), "page": page,
        "pending": counts["pending"], "inPrint": counts["in_print"], "uncertain": counts["uncertain"], "imported": counts["imported"],
        "openCount": counts["pending"] + counts["in_print"] + counts["uncertain"] + counts["unused"],
        "paperlessMax": remote_max, "nextAsnNumber": next_asn, "gap": max(0, next_asn - remote_max - 1), "checkedAt": now()}


class RateLimiter:
    def __init__(self):
        self.lock = threading.Lock()
        self.windows = OrderedDict()

    def allow(self, key, limit):
        current = int(time.monotonic() // 60)
        with self.lock:
            minute, count = self.windows.pop(key, (current, 0))
            count = count + 1 if minute == current else 1
            self.windows[key] = (current, count)
            while len(self.windows) > 4096:
                self.windows.popitem(last=False)
            return count <= limit


LIMITER = RateLimiter()


class ApiRequest:
    def __init__(self, method, path, headers, body, peer):
        self.command = method
        self.path = path
        self.headers = headers
        self.rfile = body
        self.client_address = (peer, 0)
        self.response = None
        self.route = "unknown"
        self.started = time.monotonic()

    def authorized(self, admin=False):
        header = self.headers.get("Authorization", "")
        token = header[7:] if header.startswith("Bearer ") else ""
        if not token or len(token) > 512:
            return False
        keys = CONFIG["admin_keys"] if admin else CONFIG["api_keys"] | CONFIG["admin_keys"]
        digest = hashlib.sha256(token.encode()).digest()
        matched = 0
        for key in keys:
            matched |= hmac.compare_digest(digest, hashlib.sha256(key.encode()).digest())
        if matched:
            OWNER.set(digest.hex())
            ACTOR.set(digest.hex()[:12])
        return bool(matched)

    def body(self):
        if self.headers.get("Transfer-Encoding"):
            raise ValueError("Chunked request bodies are not supported")
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 <= size <= 4096:
            raise ValueError("Invalid body size")
        if not size:
            return {}
        data = json.loads(self.rfile.read(size))
        if not isinstance(data, dict):
            raise ValueError("JSON object required")
        return data

    def dispatch(self, method):
        REQUEST_ID.set(uuid.uuid4().hex)
        ACTOR.set("anonymous")
        OWNER.set("")
        REQUEST_CLIENT_VERSION.set("")
        try:
            if method not in ("GET", "POST"):
                return self.send_json({"error": "method not allowed"}, 405)
            parsed = urlsplit(self.path)
            path = parsed.path
            downloads = {
                "/admin/license": ("licenses/LICENSE", "text/plain; charset=utf-8", None),
                "/admin/notices": ("licenses/THIRD-PARTY-NOTICES.md", "text/plain; charset=utf-8", None),
                "/admin/changelog": ("CHANGELOG.md", "text/plain; charset=utf-8", None),
                "/admin/source.zip": ("source.zip", "application/zip", "asn-reservation-source.zip"),
            }
            if method == "GET" and path in downloads:
                name, mime, filename = downloads[path]
                self.route = path
                return self.send_bytes((Path(__file__).parent / name).read_bytes(), mime, download=filename)
            static = {"/admin": ("index.html", "text/html"), "/admin/": ("index.html", "text/html"),
                      "/admin/app.js": ("app.js", "text/javascript"), "/admin/style.css": ("style.css", "text/css"),
                      "/admin/i18n.js": ("i18n.js", "text/javascript"),
                      "/admin/icon.ico": ("icon.ico", "image/x-icon"),
                      "/admin/material-icons.ttf": ("material-icons.ttf", "font/ttf"),
                      "/admin/material-icons-license.txt": ("material-icons-license.txt", "text/plain")}
            if method == "GET" and path in static:
                name, mime = static[path]
                self.route = "/admin/static"
                content = (STATIC_DIR / name).read_bytes()
                if name == "index.html":
                    content = content.replace(b"__ASN_INSTANCE__", INSTANCE_ID.encode())
                    content = content.replace(b"__ASN_VERSION__", VERSION.encode())
                return self.send_bytes(content, mime)
            if path in ("/health", "/api/reservations/next", "/api/documents/status", "/api/documents/copy-labels",
                        "/api/documents/copy-labels/start", "/api/documents/copy-labels/printed",
                        "/api/admin/reset-to-paperless", "/api/admin/state", "/api/admin/events",
                        "/api/admin/archive-preview", "/api/admin/archive-open", "/api/admin/diagnostics",
                        "/api/admin/logs", "/api/admin/backup", "/api/admin/high-water-preview", "/api/admin/high-water-correct"):
                self.route = path
            if path == "/health" and method == "GET":
                return self.send_json({"ok": True, "version": VERSION, "time": now(),
                    "apiVersion": 1,
                    "minimumClientVersion": minimum_client_version(),
                    "capabilities": ["safe-sync-v1", "physical-label-confirmation-v1", "secondary-label-tracking-v1"]})
            admin = path.startswith("/api/admin/") and path != "/api/admin/reset-to-paperless"
            if not self.authorized(admin):
                allowed = LIMITER.allow(("anonymous", self.client_address[0]), 30)
                return self.send_json({"error": "unauthorized"}, 401 if allowed else 429)
            if not LIMITER.allow(("key", OWNER.get()), 300):
                return self.send_json({"error": "rate limit; wait one minute"}, 429)
            if self.headers.get("X-ASN-API-Version", "1") != "1":
                return self.send_json({"error": "Incompatible API version. Update client/service.", "errorCode": "api_version_incompatible"}, 409)
            body = self.body() if method == "POST" else {}
            expected_version = self.headers.get("X-ASN-Version")
            if method == "POST" and expected_version and expected_version != VERSION:
                return self.send_json({"error": "Service updated. Reload the admin panel.", "errorCode": "admin_reload_required"}, 409)
            client_version = self.headers.get("X-ASN-Client-Version", "")
            REQUEST_CLIENT_VERSION.set(client_version)
            # Only real admin credentials exempt the web/CLI admin, never a UI header.
            admin_request = not client_version and self.authorized(admin=True)
            if not admin_request and not completes_existing_job(method, path):
                minimum = minimum_client_version()
                try:
                    supported = version_parts(client_version) >= version_parts(minimum)
                except ValueError:
                    supported = False
                if not supported:
                    return self.send_json({"error": "Client update required. Install a supported client version.",
                        "errorCode": "client_update_required", "minimumClientVersion": minimum}, 426)
            query = parse_qs(parsed.query)
            if path == "/api/admin/updates" and method in ("GET", "POST"):
                self.route = path
                updates = server_updates()
                return self.send_json(updates.request_refresh() if method == "POST" else updates.status())
            new_work = (path in ("/api/reservations/next", "/api/documents/copy-labels/start") or
                        re.fullmatch(r"/api/reservations/[a-zA-Z0-9-]{1,64}/printing", path))
            if method == "POST" and new_work and server_updates().status()["blocked"]:
                return self.send_json({"error": "Server security update required. No new print job started.",
                    "errorCode": "server_update_required", "serverUpdate": server_updates().status()}, 503)
            if method == "GET" and path == "/api/admin/diagnostics":
                maximum = paperless_max_asn()
                return self.send_json({"ok": True, "version": VERSION, "paperlessMax": maximum, "checkedAt": now()})
            if method == "GET" and path == "/api/admin/logs":
                with LOG_LOCK:
                    entries = list(RECENT_LOGS)
                return self.send_json({"items": entries})
            if method == "POST" and path == "/api/admin/backup":
                return self.send_bytes(backup_bytes(), "application/octet-stream", download="asn-backup-" + str(now()) + ".sqlite3")
            if path == "/api/reservations/next":
                return self.send_json(reserve_next() if method == "POST" else peek_next())
            if method == "GET" and path == "/api/documents/status":
                completed = query.get("include_completed", ["true"])[0]
                if completed not in ("true", "false"):
                    raise ValueError("include_completed must be true or false")
                hidden = query.get("hide_imported", ["false"])[0]
                if hidden not in ("true", "false"):
                    raise ValueError("hide_imported must be true or false")
                return self.send_json(document_status(int(query.get("page", ["1"])[0]), completed == "true", hidden == "true"))
            if method == "GET" and path == "/api/documents/copy-labels":
                return self.send_json(copy_label_documents(int(query.get("page", ["1"])[0]),
                    int(query["asn"][0]) if "asn" in query else None))
            if method == "POST" and path in ("/api/documents/copy-labels/start", "/api/documents/copy-labels/printed"):
                return self.send_json(secondary_label(body, path.endswith("/printed")))
            if method == "POST" and path == "/api/admin/reset-to-paperless":
                return self.send_json(reset_to_paperless())
            if method == "GET" and path == "/api/admin/state":
                hide_printed = query.get("hide_printed", ["false"])[0]
                if hide_printed not in ("true", "false"):
                    raise ValueError("hide_printed must be true or false")
                return self.send_json({**admin_state(int(query.get("page", ["1"])[0]), refresh_imports=True, hide_printed=hide_printed == "true"),
                    "serverUpdate": server_updates().status()})
            if method == "GET" and path == "/api/admin/events":
                if query.get("order", [""])[0] == "desc":
                    return self.send_json(recent_admin_events(int(query["before"][0]) if "before" in query else None))
                return self.send_json(admin_events(max(0, int(query.get("after", ["0"])[0]))))
            if method == "POST" and path == "/api/admin/archive-preview":
                return self.send_json(archive_preview())
            if method == "POST" and path == "/api/admin/high-water-preview":
                return self.send_json(high_water_preview())
            if method == "POST" and path == "/api/admin/high-water-correct":
                return self.send_json(correct_high_water(str(body.get("previewToken", "")),
                    body.get("confirmation"), body.get("physicalLabelsAbsent")))
            if method == "POST" and path == "/api/admin/archive-open":
                return self.send_json(archive_open(str(body.get("previewToken", "")), body.get("confirmation")))
            unblock = re.fullmatch(r"/api/admin/reservations/([a-zA-Z0-9-]{1,64})/unblock", path)
            if method == "POST" and unblock:
                self.route = "/api/admin/reservations/:id/unblock"
                return self.send_json(unblock_reservation(unblock[1], body.get("confirmation"), body.get("physicalLabelAbsent")))
            match = re.fullmatch(r"/api/reservations/([a-zA-Z0-9-]{1,64})/(printing|printed|release|resolve-exists|resolve-missing|resolve-missing-confirmed|cancel|cancel-confirmed)", path)
            if method == "POST" and match:
                rid, action = match.groups()
                self.route = "/api/reservations/:id/" + action
                if action in ("printing", "printed", "release"):
                    return self.send_json(transition(rid, "released" if action == "release" else action))
                return self.send_json(resolve_uncertain(rid, action == "resolve-exists",
                    cancel=action.startswith("cancel"), confirm_block=action.endswith("-confirmed"),
                    physical_label_absent=body.get("physicalLabelAbsent") is True))
            self.send_json({"error": "not found"}, 404)
        except AdmissionBlocked as exc:
            if exc.code == "client_update_required":
                return self.send_json({"error": "Client update required. No job started.", "errorCode": exc.code,
                                       "minimumClientVersion": minimum_client_version()}, 426)
            return self.send_json({"error": "New print jobs are temporarily blocked; no job started.",
                              "errorCode": exc.code}, 503)
        except KeyError:
            self.send_json({"error": "reservation not found"}, 404)
        except PermissionError:
            self.send_json({"error": "reservation belongs to a different client key"}, 403)
        except ValueError as exc:
            message = str(exc) if type(exc) is ValueError and str(exc).startswith((
                "Confirm ", "Print may", "Reservation ", "Explicit ", "Preview ", "ASN range", "JSON object", "Chunked", "Invalid body",
                "Unknown transition", "page must", "include_completed", "Backup exceeds")) else "Invalid request"
            self.send_json({"error": message}, 409 if method == "POST" else 400)
        except (BrokenPipeError, ConnectionResetError, TimeoutError):
            log("connection_lost", request_id=REQUEST_ID.get())
            self.send_json({"error": "Connection interrupted; check status before retrying"}, 503)
        except Exception as exc:
            code = exc.code if isinstance(exc, PaperlessError) else "service_or_database_error"
            log("request_failed", request_id=REQUEST_ID.get(), route=self.route, kind=type(exc).__name__,
                error_code=code,
                database_code=getattr(exc, "sqlite_errorname", None),
                upstream_status=getattr(exc.__cause__, "code", None))
            self.send_json({"error": paperless_hint(code) if isinstance(exc, PaperlessError) else
                "Service oder Datenbank nicht verfuegbar. Zustand vor erneutem Versuch pruefen.",
                "errorCode": code, "validationFields": getattr(exc, "fields", [])}, 503)

    def send_bytes(self, body, mime, download=None):
        headers = [("X-ASN-Instance", INSTANCE_ID), ("X-ASN-Version", VERSION), ("Content-Type", mime), ("Content-Length", str(len(body))), ("Cache-Control", "no-store"),
            ("X-Content-Type-Options", "nosniff"), ("Referrer-Policy", "no-referrer"),
            ("Content-Security-Policy", "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'"),
            ("X-Frame-Options", "DENY")]
        if download:
            headers.append(("Content-Disposition", 'attachment; filename="' + download + '"'))
        self.response = (200, headers, body)

    def send_json(self, payload, status=200):
        body = json.dumps({**payload, "requestId": REQUEST_ID.get()}).encode("utf-8")
        self.response = (status, [
            ("X-ASN-Instance", INSTANCE_ID), ("X-ASN-Version", VERSION),
            ("Content-Type", "application/json; charset=utf-8"),
            ("Content-Length", str(len(body))), ("Cache-Control", "no-store"),
            ("X-Content-Type-Options", "nosniff"), ("X-Request-ID", REQUEST_ID.get())], body)
        if self.route != "/health":
            log("request", request_id=REQUEST_ID.get(), actor=ACTOR.get(), method=self.command, route=self.route,
                status=status, duration_ms=round((time.monotonic() - self.started) * 1000))


def wsgi_app(environ, start_response):
    headers = {"Authorization": environ.get("HTTP_AUTHORIZATION", ""),
               "X-ASN-Instance": environ.get("HTTP_X_ASN_INSTANCE", ""),
               "X-ASN-Version": environ.get("HTTP_X_ASN_VERSION", ""),
               "X-ASN-Client-Version": environ.get("HTTP_X_ASN_CLIENT_VERSION", ""),
               "X-ASN-API-Version": environ.get("HTTP_X_ASN_API_VERSION", "1"),
               "Content-Length": environ.get("CONTENT_LENGTH") or "0",
               "Transfer-Encoding": environ.get("HTTP_TRANSFER_ENCODING", "")}
    path = environ.get("PATH_INFO", "/")
    if environ.get("QUERY_STRING"):
        path += "?" + environ["QUERY_STRING"]
    api = ApiRequest(environ["REQUEST_METHOD"], path, headers, environ["wsgi.input"], environ.get("REMOTE_ADDR", "unknown"))
    api.dispatch(api.command)
    status, response_headers, body = api.response
    start_response(str(status) + " " + HTTPStatus(status).phrase, response_headers)
    return [body]


class Handler(BaseHTTPRequestHandler):
    """Standard-library adapter used only by isolated integration tests."""
    def setup(self):
        super().setup()
        self.connection.settimeout(10)

    def do_GET(self):
        self.dispatch()

    def do_POST(self):
        self.dispatch()

    def dispatch(self):
        api = ApiRequest(self.command, self.path, self.headers, self.rfile, self.client_address[0])
        api.dispatch(self.command)
        status, headers, body = api.response
        self.send_response(status)
        for key, value in headers:
            self.send_header(key, value)
        self.send_header("Connection", "close")
        self.end_headers()
        self.close_connection = True
        self.wfile.write(body)

    def log_message(self, fmt, *args):
        pass


class BoundedServer(ThreadingHTTPServer):
    daemon_threads = True
    request_queue_size = 32

    def __init__(self, *args, **kwargs):
        self.slots = threading.BoundedSemaphore(16)
        super().__init__(*args, **kwargs)

    def process_request(self, req, address):
        if not self.slots.acquire(blocking=False):
            self.shutdown_request(req)
            return
        try:
            super().process_request(req, address)
        except Exception:
            self.slots.release()
            raise

    def process_request_thread(self, req, address):
        try:
            super().process_request_thread(req, address)
        finally:
            self.slots.release()


def validate_config():
    version_parts(CLIENT_VERSION_FLOOR)
    if not CONFIG["api_keys"] or any(not 32 <= len(k) <= 512 or k.startswith("replace-") for k in CONFIG["api_keys"] | CONFIG["admin_keys"]):
        raise RuntimeError("API_KEYS required; each client/admin key must be at least 32 characters")
    if CONFIG["api_keys"] & CONFIG["admin_keys"]:
        raise RuntimeError("Administrator and client keys must differ")
    url = urlsplit(CONFIG["paperless_url"])
    if url.scheme not in ("http", "https") or not url.hostname or url.username or url.password or url.query or url.fragment:
        raise RuntimeError("PAPERLESS_URL must be an HTTP(S) base URL without credentials/query")
    if not CONFIG["paperless_token"]:
        raise RuntimeError("PAPERLESS_TOKEN required")
    if not 30 <= CONFIG["reservation_ttl"] <= 3600 or not 10 <= CONFIG["printing_settle"] <= 86400:
        raise RuntimeError("Invalid reservation TTL or printing settle time")
    if not re.fullmatch(r"[A-Za-z][A-Za-z0-9]{0,15}", CONFIG["asn_prefix"]) or not 1 <= CONFIG["asn_padding"] <= 10:
        raise RuntimeError("Invalid ASN prefix/padding")


def main():
    from waitress import serve
    validate_config()
    backup_before_migration()
    db_connect().close()
    server_updates().start()
    log("service_started", version=VERSION, schema=3, ttl=CONFIG["reservation_ttl"],
        minimum_client_version=minimum_client_version(),
        admin_enabled=bool(CONFIG["admin_keys"]), admin_path="/admin/", message="ASN-Service bereit; Paperless wird bei der ersten Abfrage geprueft.")
    serve(wsgi_app, host=CONFIG["listen_addr"], port=CONFIG["port"], threads=8,
          connection_limit=64, backlog=32, channel_timeout=15, cleanup_interval=5,
          max_request_header_size=16384, max_request_body_size=4096,
          expose_tracebacks=False, clear_untrusted_proxy_headers=True, log_socket_errors=False)


if __name__ == "__main__":
    main()
