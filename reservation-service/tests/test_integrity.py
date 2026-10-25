import io
import json
import os
import sqlite3
import tempfile
import threading
import unittest
from contextlib import closing
from concurrent.futures import ThreadPoolExecutor
from unittest import mock
from urllib import request
from urllib.error import HTTPError

from test_app import load_app


class IntegrityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = os.path.join(self.temp.name, "db.sqlite3")
        self.app = load_app(self.path)
        self.remote = mock.patch.object(self.app, "paperless_max_asn", return_value=0)
        self.maximum = self.remote.start()
        self.addCleanup(self.remote.stop)
        self.docs = mock.patch.object(self.app, "paperless_documents", return_value={"results": []})
        self.docs.start()
        self.addCleanup(self.docs.stop)

    def state(self, rid):
        conn = self.app.db_connect()
        try:
            row = conn.execute("SELECT status FROM reservations WHERE id=?", (rid,)).fetchone()
            return row[0] if row else None
        finally:
            conn.close()

    def allocate(self, state="reserved"):
        row = self.app.reserve_next()
        if state in ("printing", "printed"):
            self.app.transition(row["reservationId"], "printing")
        if state == "printed":
            self.app.transition(row["reservationId"], "printed")
        return row

    def test_ttl_reclaims_only_never_started_and_invalidates_old_id(self):
        with mock.patch.object(self.app, "now", return_value=1000):
            old = self.allocate()
        with mock.patch.object(self.app, "now", return_value=1300):
            with self.assertRaisesRegex(ValueError, "expired"):
                self.app.transition(old["reservationId"], "printing")
            replacement = self.allocate()
            self.assertEqual(replacement["asnNumber"], old["asnNumber"])
            self.assertNotEqual(replacement["reservationId"], old["reservationId"])
            with self.assertRaises(KeyError):
                self.app.transition(old["reservationId"], "printing")

    def test_crash_after_printing_never_expires(self):
        with mock.patch.object(self.app, "now", return_value=1000):
            old = self.allocate("printing")
        with mock.patch.object(self.app, "now", return_value=999999):
            self.assertEqual(self.app.peek_next()["asnNumber"], 2)
            self.assertEqual(self.state(old["reservationId"]), "printing")
            self.app.reset_to_paperless()
            self.assertEqual(self.state(old["reservationId"]), "printing")

    def test_expiry_and_start_are_serialized(self):
        with mock.patch.object(self.app, "now", return_value=1000):
            old = self.allocate()
        def start():
            try:
                self.app.transition(old["reservationId"], "printing")
                return "started"
            except (ValueError, KeyError):
                return "rejected"
        with mock.patch.object(self.app, "now", return_value=1300), ThreadPoolExecutor(2) as pool:
            first = pool.submit(start)
            second = pool.submit(self.app.reserve_next)
            self.assertEqual(first.result(), "rejected")
            self.assertEqual(second.result()["asnNumber"], 1)

    def test_safe_sync_and_print_start_race(self):
        for _ in range(10):
            row = self.allocate()
            def start():
                try:
                    self.app.transition(row["reservationId"], "printing")
                    return True
                except ValueError:
                    return False
            with ThreadPoolExecutor(2) as pool:
                start_result = pool.submit(start)
                sync = pool.submit(self.app.reset_to_paperless)
                started = start_result.result()
                sync.result()
            self.assertEqual(self.state(row["reservationId"]), "printing" if started else "reusable")

    def test_reset_preserves_printed_blocked_and_uncertain(self):
        a, b, c = self.allocate("printed"), self.allocate("printing"), self.allocate()
        self.app.transition(c["reservationId"], "released")
        self.app.reset_to_paperless()
        self.assertEqual(self.state(a["reservationId"]), "printed")
        self.assertEqual(self.state(b["reservationId"]), "printing")
        self.assertEqual(self.app.reserve_next()["asnNumber"], 3)
        self.maximum.return_value = 10
        self.app.reset_to_paperless()
        self.assertEqual(self.app.peek_next()["asnNumber"], 11)

    def test_tail_can_be_freed_from_highest_to_lowest(self):
        rows = [self.allocate("printed") for _ in range(3)]
        for row in reversed(rows):
            result = self.app.resolve_uncertain(row["reservationId"], False, cancel=True, physical_label_absent=True)
            self.assertEqual(result["status"], "next")
            self.assertEqual(self.app.peek_next()["asnNumber"], row["asnNumber"])
        self.assertEqual(self.allocate()["asnNumber"], 1)

    def test_lower_printed_cannot_be_reused_with_higher_print(self):
        low = self.allocate("printed")
        self.allocate("printed")
        result = self.app.resolve_uncertain(low["reservationId"], False, cancel=True, physical_label_absent=True)
        self.assertEqual(result["status"], "confirmation_required")
        self.assertEqual(self.state(low["reservationId"]), "printed")
        self.assertEqual(self.app.resolve_uncertain(low["reservationId"], False, cancel=True,
            confirm_block=True, physical_label_absent=True)["status"], "blocked")
        self.assertEqual(self.app.peek_next()["asnNumber"], 3)

    def test_destroyed_confirmation_and_inflight_holdoff_required(self):
        row = self.allocate("printing")
        self.app.CONFIG["printing_settle"] = 120
        with self.assertRaisesRegex(ValueError, "physicalLabelAbsent"):
            self.app.resolve_uncertain(row["reservationId"], False)
        with self.assertRaisesRegex(ValueError, "Print may"):
            self.app.resolve_uncertain(row["reservationId"], False, physical_label_absent=True)
        self.assertEqual(self.state(row["reservationId"]), "printing")

    def test_remote_maximum_never_moves_backwards_after_document_deleted(self):
        self.maximum.return_value = 50
        self.assertEqual(self.app.peek_next()["asnNumber"], 51)
        self.maximum.return_value = 0
        self.assertEqual(self.app.reset_to_paperless()["nextAsnNumber"], 51)

    def test_offline_operations_leave_state_and_audit_unchanged(self):
        row = self.allocate("printed")
        before = self.app.admin_state()
        self.maximum.side_effect = RuntimeError("offline")
        for operation in (self.app.reserve_next, self.app.peek_next, self.app.reset_to_paperless, self.app.archive_preview):
            with self.assertRaises(RuntimeError):
                operation()
        self.assertEqual(self.app.admin_state(), before)
        self.assertEqual(self.state(row["reservationId"]), "printed")

    def test_audit_failure_rolls_back_allocation(self):
        self.app.db_connect().close()
        with mock.patch.object(self.app, "audit", side_effect=RuntimeError("disk")):
            with self.assertRaises(RuntimeError):
                self.app.reserve_next()
        self.assertEqual(self.app.admin_state()["total"], 0)

    def test_unique_and_state_invariants_in_database(self):
        self.allocate()
        conn = self.app.db_connect()
        try:
            with self.assertRaises(sqlite3.IntegrityError):
                conn.execute("INSERT INTO reservations SELECT 'other', asn_number, barcode_text,status,created_at,updated_at,expires_at,owner FROM reservations")
            with self.assertRaises(sqlite3.IntegrityError):
                conn.execute("UPDATE reservations SET status='unexpected'")
        finally:
            conn.close()

    def test_printing_is_not_idempotent_but_printed_ack_is(self):
        row = self.allocate("printing")
        with self.assertRaises(ValueError):
            self.app.transition(row["reservationId"], "printing")
        self.app.transition(row["reservationId"], "printed")
        count = len(self.app.admin_events()["items"])
        self.app.transition(row["reservationId"], "printed")
        self.assertEqual(len(self.app.admin_events()["items"]), count)

    def test_admin_archive_requires_fresh_preview_and_never_frees(self):
        row = self.allocate("printed")
        preview = self.app.archive_preview()
        with self.assertRaises(ValueError):
            self.app.archive_open(preview["previewToken"], "YES")
        self.allocate()
        with self.assertRaisesRegex(ValueError, "Preview"):
            self.app.archive_open(preview["previewToken"], "BLOCK OUTSTANDING")
        preview = self.app.archive_preview()
        self.assertEqual(self.app.archive_open(preview["previewToken"], "BLOCK OUTSTANDING")["blocked"], 2)
        self.assertEqual(self.state(row["reservationId"]), "blocked")
        self.assertEqual(self.app.peek_next()["asnNumber"], 3)
        with self.assertRaises(ValueError):
            self.app.archive_open(preview["previewToken"], "BLOCK OUTSTANDING")

    def test_admin_preview_expiry(self):
        with mock.patch.object(self.app, "now", return_value=1000):
            preview = self.app.archive_preview()
        with mock.patch.object(self.app, "now", return_value=1120):
            with self.assertRaises(ValueError):
                self.app.archive_open(preview["previewToken"], "BLOCK OUTSTANDING")

    def test_archive_uses_exact_imports_and_handles_lower_gaps(self):
        first, second = self.allocate("printed"), self.allocate("printed")
        self.maximum.return_value = 2
        with mock.patch.object(self.app, "paperless_documents",
                return_value={"results": [{"archive_serial_number": 2}], "next": None}):
            preview = self.app.archive_preview()
            self.assertEqual(preview["count"], 1)
            self.app.archive_open(preview["previewToken"], "BLOCK OUTSTANDING")
            self.assertEqual(self.state(first["reservationId"]), "blocked")
            self.assertEqual(self.state(second["reservationId"]), "printed")
            self.assertEqual(self.app.document_status(include_completed=False)["openCount"], 0)

    def test_archive_rechecks_paperless_between_confirmations(self):
        row = self.allocate("printed")
        preview = self.app.archive_preview()
        with mock.patch.object(self.app, "paperless_documents",
                return_value={"results": [{"archive_serial_number": 1}], "next": None}):
            with self.assertRaisesRegex(ValueError, "Preview"):
                self.app.archive_open(preview["previewToken"], "BLOCK OUTSTANDING")
        self.assertEqual(self.state(row["reservationId"]), "printed")

    def test_ttl_does_not_reuse_below_higher_printed(self):
        with mock.patch.object(self.app, "now", return_value=1000):
            first = self.allocate()
            self.allocate("printed")
        with mock.patch.object(self.app, "now", return_value=1300):
            self.assertEqual(self.app.peek_next()["asnNumber"], 3)
            self.assertEqual(self.state(first["reservationId"]), "blocked")

    def test_rate_limit_is_bounded_and_resets(self):
        limiter = self.app.RateLimiter()
        with mock.patch.object(self.app.time, "monotonic", return_value=60):
            self.assertTrue(limiter.allow("key", 2))
            self.assertTrue(limiter.allow("key", 2))
            self.assertFalse(limiter.allow("key", 2))
            for key in range(5000):
                limiter.allow(key, 2)
            self.assertEqual(len(limiter.windows), 4096)
        with mock.patch.object(self.app.time, "monotonic", return_value=120):
            self.assertTrue(limiter.allow("key", 2))

    def test_concurrent_clients_reuse_tail_only_once(self):
        row = self.allocate()
        self.app.transition(row["reservationId"], "released")
        with ThreadPoolExecutor(8) as pool:
            results = list(pool.map(lambda _: self.app.reserve_next(), range(32)))
        self.assertEqual(sorted(r["asnNumber"] for r in results), list(range(1, 33)))
        events = self.app.admin_events()["items"]
        self.assertTrue(any(e["reservation_id"] == row["reservationId"] and e["reason"] == "client_released" for e in events))


class MigrationTests(unittest.TestCase):
    def legacy(self, path, rows):
        with closing(sqlite3.connect(path)) as conn, conn:
            conn.execute("""CREATE TABLE reservations (id TEXT PRIMARY KEY, asn_number INTEGER NOT NULL,
                barcode_text TEXT NOT NULL, status TEXT NOT NULL, created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL, expires_at INTEGER NOT NULL)""")
            conn.executemany("INSERT INTO reservations VALUES (?,?,?,?,0,0,0)", rows)

    def test_legacy_release_and_reserved_migrate_without_erasing_evidence(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "db")
            self.legacy(path, [("a", 1, "ASN00001", "released"), ("b", 2, "ASN00002", "reserved"),
                               ("c", 3, "ASN00003", "printing"), ("d", 4, "ASN00004", "unknown")])
            app = load_app(path)
            conn = app.db_connect()
            try:
                rows = {r["id"]: dict(r) for r in conn.execute("SELECT * FROM reservations")}
                self.assertEqual(rows["a"]["status"], "reusable")
                self.assertGreater(rows["b"]["expires_at"], app.now())
                self.assertEqual(rows["c"]["status"], "printing")
                self.assertEqual(rows["d"]["status"], "blocked")
                self.assertEqual(conn.execute("SELECT COUNT(*) FROM audit_events").fetchone()[0], 4)
            finally:
                conn.close()
            app.db_connect().close()
            self.assertEqual(len(app.admin_events()["items"]), 4)

    def test_duplicate_migration_stops_without_modifying_database(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "db")
            self.legacy(path, [("a", 1, "ASN00001", "printed"), ("b", 1, "ASN00001", "printing")])
            app = load_app(path)
            with self.assertRaisesRegex(RuntimeError, "duplicate"):
                app.db_connect()
            with closing(sqlite3.connect(path)) as conn:
                self.assertEqual(conn.execute("SELECT COUNT(*) FROM reservations").fetchone()[0], 2)
                self.assertEqual(conn.execute("PRAGMA user_version").fetchone()[0], 0)


class HttpIntegrityTests(unittest.TestCase):
    allocate = IntegrityTests.allocate
    state = IntegrityTests.state

    def setUp(self):
        IntegrityTests.setUp(self)
        self.app.CONFIG["admin_keys"] = {"admin-secret"}
        self.app.CONFIG["api_keys"] = {"secret", "other-client"}
        self.server = self.app.BoundedServer(("127.0.0.1", 0), self.app.Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.stop_server)
        self.base = "http://127.0.0.1:" + str(self.server.server_port)

    def stop_server(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join()

    def call(self, path, method="GET", body=None, key="secret"):
        req = request.Request(self.base + path, method=method,
            data=None if body is None else json.dumps(body).encode(),
            headers={"Authorization": "Bearer " + key, "X-ASN-Client-Version": "2.1.1"})
        try:
            with request.urlopen(req, timeout=3) as response:
                return response.status, json.load(response), response.headers
        except HTTPError as response:
            return response.code, json.load(response), response.headers

    def test_keys_owner_and_admin_permissions(self):
        status, row, headers = self.call("/api/reservations/next", "POST")
        self.assertEqual(status, 200)
        self.assertEqual(headers["Cache-Control"], "no-store")
        rid = row["reservationId"]
        self.assertEqual(self.call("/api/reservations/" + rid + "/printing", "POST", key="other-client")[0], 403)
        self.assertEqual(self.call("/api/reservations/" + rid + "/printing", "POST")[0], 200)
        self.assertEqual(self.call("/api/admin/state")[0], 401)
        self.assertEqual(self.call("/api/admin/state", key="admin-secret")[0], 200)
        self.assertEqual(self.call("/api/admin/reset-to-paperless", "POST")[0], 200)
        self.assertEqual(self.call("/api/admin/state", key="bad")[0], 401)
        serialized = json.dumps(self.app.log.call_args_list, default=str)
        self.assertNotIn("admin-secret", serialized)
        self.assertNotIn("other-client", serialized)
        self.assertNotIn("Bearer", serialized)

    def test_no_body_cannot_release_printed_or_uncertain(self):
        row = self.allocate("printed")
        path = "/api/reservations/" + row["reservationId"] + "/cancel"
        self.assertEqual(self.call(path, "POST")[0], 409)
        self.assertEqual(self.call(path, "POST", {"physicalLabelAbsent": "true"})[0], 409)
        self.assertEqual(self.call(path, "POST", {"physicalLabelAbsent": True})[1]["status"], "next")

    def test_malformed_payload_and_unknown_routes(self):
        req = request.Request(self.base + "/api/reservations/next", method="POST", data=b"{bad",
                              headers={"Authorization": "Bearer secret", "X-ASN-Client-Version": "2.1.1"})
        with self.assertRaises(HTTPError) as exc:
            request.urlopen(req, timeout=3)
        self.assertEqual(exc.exception.code, 409)
        self.assertEqual(self.app.admin_state()["total"], 0)
        self.assertEqual(self.call("/api/missing")[0], 404)

    def test_offline_is_503_with_request_id_and_no_secrets(self):
        self.maximum.side_effect = RuntimeError("Bearer token-secret https://internal-private")
        status, body, headers = self.call("/api/reservations/next", "POST")
        self.assertEqual(status, 503)
        self.assertEqual(body["requestId"], headers["X-Request-ID"])
        self.assertNotIn("token-secret", json.dumps(body))
        self.assertNotIn("internal-private", json.dumps(body))

    def test_constant_time_comparison_visits_all_keys(self):
        with mock.patch.object(self.app.hmac, "compare_digest", wraps=self.app.hmac.compare_digest) as comparison:
            self.call("/api/admin/state", key="admin-secret")
            self.assertEqual(comparison.call_count, 1)
            self.call("/api/reservations/next")
            self.assertEqual(comparison.call_count, 4)


if __name__ == "__main__":
    unittest.main()
