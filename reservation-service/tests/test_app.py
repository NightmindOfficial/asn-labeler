import importlib.util
import io
import json
import os
import tempfile
import threading
import unittest
from unittest import mock
from http.server import ThreadingHTTPServer
from urllib import request
from urllib.error import HTTPError
from urllib.parse import parse_qs, urlparse
from concurrent.futures import ThreadPoolExecutor


APP_PATH = os.path.join(os.path.dirname(__file__), "..", "app.py")


def load_app(db_path):
    spec = importlib.util.spec_from_file_location("reservation_app", APP_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.CONFIG.update(
        {
            "paperless_url": "http://paperless.test",
            "paperless_token": "token",
            "asn_prefix": "ASN",
            "asn_padding": 5,
            "api_keys": {"secret"},
            "sqlite_path": db_path,
            "printing_settle": 0,  # Legacy flow tests; real holdoff covered in integrity tests.
        }
    )
    module.log = mock.Mock()  # Keep the suite quiet; audit rows remain real.
    return module


class ReservationTests(unittest.TestCase):
    def test_cancel_pending_reuses_or_blocks_and_never_cancels_imported(self):
        for remote, found, expected in [(0, False, "next"), (2, False, "blocked"), (1, True, "imported")]:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temp:
                app = load_app(os.path.join(temp, "db.sqlite3"))
                with mock.patch.object(app, "paperless_max_asn", return_value=0):
                    row = app.reserve_next()
                app.transition(row["reservationId"], "printing")
                app.transition(row["reservationId"], "printed")
                with mock.patch.object(app, "paperless_max_asn", return_value=remote), mock.patch.object(app, "paperless_documents", return_value={"results": [{"archive_serial_number": 1}] if found else []}):
                    result = app.resolve_uncertain(row["reservationId"], False, cancel=True, physical_label_absent=True)
                    self.assertEqual(result["status"], expected)
                    self.assertEqual(app.document_status(include_completed=False)["openCount"], 0)
                    if expected == "next": self.assertEqual(app.reserve_next()["asnNumber"], 1)

    def test_cancel_higher_local_requires_confirmation_and_rechecks(self):
        for higher_state in ("reserved", "printing", "printed"):
            with self.subTest(higher_state=higher_state), tempfile.TemporaryDirectory() as temp:
                app = load_app(os.path.join(temp, "db.sqlite3"))
                with mock.patch.object(app, "paperless_max_asn", return_value=0), mock.patch.object(app, "paperless_documents", return_value={"results": []}):
                    first = app.reserve_next()
                    app.transition(first["reservationId"], "printing")
                    app.transition(first["reservationId"], "printed")
                    second = app.reserve_next()
                    if higher_state != "reserved": app.transition(second["reservationId"], "printing")
                    if higher_state == "printed": app.transition(second["reservationId"], "printed")
                    self.assertEqual(app.resolve_uncertain(first["reservationId"], False, cancel=True, physical_label_absent=True)["status"], "confirmation_required")
                    self.assertEqual(app.document_status()["pending"], 2 if higher_state == "printed" else 1)
                    self.assertEqual(app.resolve_uncertain(first["reservationId"], False, cancel=True, confirm_block=True, physical_label_absent=True)["status"], "blocked")
                    with self.assertRaises(ValueError): app.resolve_uncertain(first["reservationId"], False, cancel=True, confirm_block=True, physical_label_absent=True)

    def test_cancel_offline_and_import_between_confirmations(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                first = app.reserve_next()
                app.reserve_next()
            app.transition(first["reservationId"], "printing")
            app.transition(first["reservationId"], "printed")
            with mock.patch.object(app, "paperless_documents", side_effect=RuntimeError("offline")):
                with self.assertRaises(RuntimeError): app.resolve_uncertain(first["reservationId"], False, cancel=True, physical_label_absent=True)
            with mock.patch.object(app, "paperless_max_asn", return_value=0), mock.patch.object(app, "paperless_documents", return_value={"results": []}):
                self.assertEqual(app.resolve_uncertain(first["reservationId"], False, cancel=True, physical_label_absent=True)["status"], "confirmation_required")
            with mock.patch.object(app, "paperless_max_asn", return_value=1), mock.patch.object(app, "paperless_documents", return_value={"results": [{"archive_serial_number": 1}]}):
                self.assertEqual(app.resolve_uncertain(first["reservationId"], False, cancel=True, confirm_block=True, physical_label_absent=True)["status"], "imported")

    def test_http_resolve_auth_success_and_stale_request(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                row = app.reserve_next()
            app.transition(row["reservationId"], "printing")
            server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
            threading.Thread(target=server.serve_forever, daemon=True).start()
            url = f"http://127.0.0.1:{server.server_port}/api/reservations/{row['reservationId']}/resolve-missing"
            try:
                with self.assertRaises(HTTPError) as exc:
                    request.urlopen(request.Request(url, method="POST"), timeout=3)
                self.assertEqual(exc.exception.code, 401)
                req = request.Request(url, method="POST", data=b'{"physicalLabelAbsent":true}', headers={"Authorization": "Bearer secret"})
                with mock.patch.object(app, "paperless_max_asn", return_value=0), mock.patch.object(app, "paperless_documents", return_value={"results": []}):
                    with request.urlopen(req, timeout=3) as response:
                        self.assertEqual(json.load(response)["status"], "next")
                    with self.assertRaises(HTTPError) as exc: request.urlopen(req, timeout=3)
                    self.assertEqual(exc.exception.code, 409)
            finally:
                server.shutdown()
                server.server_close()

    def test_resolve_uncertain_outcomes_and_badge(self):
        for exists, remote, found, expected in [(True, 0, False, "pending"), (False, 2, False, "blocked"), (False, 1, True, "imported"), (False, 0, False, "next")]:
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as temp:
                app = load_app(os.path.join(temp, "db.sqlite3"))
                with mock.patch.object(app, "paperless_max_asn", return_value=0):
                    reservation = app.reserve_next()
                app.transition(reservation["reservationId"], "printing")
                docs = {"results": [{"archive_serial_number": 1}] if found else []}
                with mock.patch.object(app, "paperless_max_asn", return_value=remote), mock.patch.object(app, "paperless_documents", return_value=docs):
                    self.assertEqual(app.resolve_uncertain(reservation["reservationId"], exists, physical_label_absent=True)["status"], expected)
                    status = app.document_status(include_completed=False)
                    self.assertEqual(status["openCount"], 1 if expected == "pending" else 0)
                    if expected == "next":
                        self.assertEqual(status["total"], 0)
                        self.assertEqual(app.peek_next()["asnNumber"], 1)
                        replacement = app.reserve_next()
                        self.assertEqual(replacement["asnNumber"], 1)
                        self.assertNotEqual(replacement["reservationId"], reservation["reservationId"])
                        with self.assertRaises(KeyError): app.transition(reservation["reservationId"], "printed")
                    else:
                        with self.assertRaises(ValueError): app.resolve_uncertain(reservation["reservationId"], exists, physical_label_absent=True)

    def test_resolution_offline_preserves_uncertain(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                row = app.reserve_next()
            app.transition(row["reservationId"], "printing")
            with mock.patch.object(app, "paperless_documents", side_effect=RuntimeError("offline")):
                with self.assertRaises(RuntimeError): app.resolve_uncertain(row["reservationId"], False, physical_label_absent=True)
            conn = app.db_connect()
            self.assertEqual(conn.execute("SELECT status FROM reservations").fetchone()[0], "printing")
            conn.close()

    def test_reuse_is_unique_with_concurrent_reservations_and_remote_advance(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0), mock.patch.object(app, "paperless_documents", return_value={"results": []}):
                first = app.reserve_next()
                app.transition(first["reservationId"], "printing")
                app.resolve_uncertain(first["reservationId"], False, physical_label_absent=True)
                with ThreadPoolExecutor(max_workers=4) as pool:
                    numbers = list(pool.map(lambda _: app.reserve_next()["asnNumber"], range(4)))
                self.assertEqual(sorted(numbers), [1, 2, 3, 4])
                latest = app.reserve_next()
                app.transition(latest["reservationId"], "printing")
                app.resolve_uncertain(latest["reservationId"], False, physical_label_absent=True)
            with mock.patch.object(app, "paperless_max_asn", return_value=10):
                self.assertEqual(app.reserve_next()["asnNumber"], 11)

    def test_hide_completed_before_pagination_without_releasing_asns(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                for _ in range(13):
                    app.reserve_next()
            docs = {"results": [{"archive_serial_number": i} for i in range(1, 13)], "next": None}
            with mock.patch.object(app, "paperless_max_asn", return_value=12), mock.patch.object(app, "paperless_documents", return_value=docs):
                result = app.document_status(include_completed=False)
                self.assertEqual(result["total"], 1)
                self.assertEqual(result["items"][0]["asnNumber"], 13)
                self.assertEqual(result["imported"], 12)
                self.assertEqual(result["nextAsnNumber"], 14)
                self.assertEqual(app.document_status()["total"], 13)
                self.assertEqual(app.document_status(2, False)["items"], [])
                self.assertEqual(app.reserve_next()["asnNumber"], 14)

    def test_document_status_uses_exact_asns_not_maximum_gap(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                rows = [app.reserve_next() for _ in range(7)]
            for number in (1, 2, 4, 7):
                app.transition(rows[number - 1]["reservationId"], "printing")
                app.transition(rows[number - 1]["reservationId"], "printed")
            app.transition(rows[4]["reservationId"], "printing")
            with mock.patch.object(app, "paperless_max_asn", return_value=4):
                app.transition(rows[5]["reservationId"], "released")
            docs = {"results": [{"archive_serial_number": 1, "title": "First"}, {"archive_serial_number": 4, "title": "Fourth"}], "next": None}
            with mock.patch.object(app, "paperless_max_asn", return_value=4), mock.patch.object(app, "paperless_documents", return_value=docs):
                result = app.document_status()
            states = {i["asnNumber"]: i["status"] for i in result["items"]}
            self.assertEqual(states, {1: "imported", 2: "pending", 3: "unused", 4: "imported", 5: "uncertain", 6: "blocked", 7: "pending"})
            self.assertEqual(result["pending"], 2)
            self.assertEqual(result["gap"], 3)
            self.assertEqual(result["items"][0]["asnNumber"], 7)

    def test_document_status_queries_all_remote_pages(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                for _ in range(3): app.reserve_next()
            responses = [{"results": [{"archive_serial_number": 1}], "next": "http://untrusted.test/"},
                         {"results": [{"archive_serial_number": 3}], "next": None}]
            with mock.patch.object(app, "paperless_max_asn", return_value=3), mock.patch.object(app, "paperless_documents", side_effect=responses) as api:
                result = app.document_status()
            self.assertEqual(result["imported"], 2)
            self.assertEqual(api.call_args_list[1].args[0]["archive_serial_number__gt"], 1)

    def test_document_status_offline_is_not_reported_as_pending(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", side_effect=RuntimeError("offline")):
                with self.assertRaises(RuntimeError): app.document_status()

    def test_document_status_pagination_and_empty_history(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0), mock.patch.object(app, "paperless_documents", return_value={"results": []}):
                self.assertEqual(app.document_status()["items"], [])
                for _ in range(13): app.reserve_next()
                self.assertEqual(len(app.document_status()["items"]), 10)
                self.assertEqual(len(app.document_status(2)["items"]), 3)
                self.assertEqual(app.document_status()["pending"], 0)
                with self.assertRaises(ValueError): app.document_status(0)

    def test_document_status_endpoint_auth_and_failure(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
            threading.Thread(target=server.serve_forever, daemon=True).start()
            url = f"http://127.0.0.1:{server.server_port}/api/documents/status"
            try:
                with self.assertRaises(HTTPError) as exc: request.urlopen(url, timeout=3)
                self.assertEqual(exc.exception.code, 401)
                req = request.Request(url, headers={"Authorization": "Bearer secret"})
                with mock.patch.object(app, "paperless_max_asn", return_value=0):
                    with request.urlopen(req, timeout=3) as response:
                        self.assertEqual(json.load(response)["pending"], 0)
                with mock.patch.object(app, "paperless_max_asn", side_effect=RuntimeError("offline")):
                    with self.assertRaises(HTTPError) as exc: request.urlopen(req, timeout=3)
                    self.assertEqual(exc.exception.code, 503)
            finally:
                server.shutdown()
                server.server_close()

    def test_remote_query_excludes_missing_asns(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app.request.OpenerDirector, "open", return_value=io.BytesIO(json.dumps({"results": [{"archive_serial_number": 81}]}).encode())) as call:
                self.assertEqual(app.paperless_max_asn(), 81)
            req = call.call_args.args[0]
            query = parse_qs(urlparse(req.full_url).query)
            self.assertEqual(query["archive_serial_number__gt"], ["0"])
            self.assertNotIn("archive_serial_number__isnull", query)
            self.assertEqual(query["ordering"], ["-archive_serial_number"])
            self.assertEqual(req.get_header("Authorization"), "Token token")

    def test_concurrent_reservations_are_unique(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            app.db_connect().close()
            with mock.patch.object(app, "paperless_max_asn", return_value=10):
                with ThreadPoolExecutor(max_workers=8) as pool:
                    results = list(pool.map(lambda _: app.reserve_next(), range(24)))
            self.assertEqual(sorted(r["asnNumber"] for r in results), list(range(11, 35)))

    def test_printed_requires_printing_and_cannot_restart(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                result = app.reserve_next()
            rid = result["reservationId"]
            with self.assertRaises(ValueError): app.transition(rid, "printed")
            app.transition(rid, "printing")
            app.transition(rid, "printed")
            with self.assertRaises(ValueError): app.transition(rid, "printing")

    def test_first_reservation_uses_remote_max_plus_one(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=41):
                result = app.reserve_next()
            self.assertEqual(result["asnNumber"], 42)
            self.assertEqual(result["barcodeText"], "ASN00042")

    def test_active_reservation_advances_next_number(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                first = app.reserve_next()
                second = app.reserve_next()
            self.assertEqual(first["asnNumber"], 1)
            self.assertEqual(second["asnNumber"], 2)

    def test_expired_reserved_number_is_reused(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                first = app.reserve_next()
            conn = app.db_connect()
            conn.execute("UPDATE reservations SET expires_at = ? WHERE id = ?", (app.now() - 1, first["reservationId"]))
            conn.close()
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                second = app.reserve_next()
            self.assertEqual(second["asnNumber"], first["asnNumber"])

    def test_peek_next_does_not_reserve(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=4):
                preview = app.peek_next()
                first = app.reserve_next()
            self.assertEqual(preview["asnNumber"], 5)
            self.assertEqual(first["asnNumber"], 5)
            conn = app.db_connect()
            count = conn.execute("SELECT COUNT(*) AS count FROM reservations").fetchone()["count"]
            conn.close()
            self.assertEqual(count, 1)

    def test_printing_number_is_not_reused_after_old_expiry_value(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                first = app.reserve_next()
            app.transition(first["reservationId"], "printing")
            conn = app.db_connect()
            conn.execute("UPDATE reservations SET expires_at = ? WHERE id = ?", (app.now() - 1, first["reservationId"]))
            conn.close()
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                second = app.reserve_next()
            self.assertEqual(second["asnNumber"], first["asnNumber"] + 1)

    def test_released_number_is_reused(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                first = app.reserve_next()
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                app.transition(first["reservationId"], "released")
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                second = app.reserve_next()
            self.assertEqual(second["asnNumber"], first["asnNumber"])

    def test_reset_to_paperless_preserves_history_and_releases_unused_tail(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                app.reserve_next()
                app.reserve_next()
                app.reserve_next()
            with mock.patch.object(app, "paperless_max_asn", return_value=1):
                result = app.reset_to_paperless()
            self.assertEqual(result["paperlessMax"], 1)
            self.assertEqual(result["deletedReservations"], 0)
            self.assertEqual(result["releasedReservations"], 2)
            self.assertEqual(result["invalidatedReservations"], 3)
            self.assertEqual(result["nextAsnNumber"], 2)
            self.assertEqual(result["nextBarcodeText"], "ASN00002")

    def test_release_only_allows_reserved(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                first = app.reserve_next()
            app.transition(first["reservationId"], "printing")
            with self.assertRaises(ValueError):
                app.transition(first["reservationId"], "released")

    def test_http_api_requires_bearer_token(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            app.db_connect().close()
            server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            url = f"http://127.0.0.1:{server.server_port}/api/reservations/next"
            try:
                with self.assertRaises(HTTPError) as ctx:
                    request.urlopen(request.Request(url, method="POST"), timeout=3)
                self.assertEqual(ctx.exception.code, 401)
            finally:
                server.shutdown()
                server.server_close()

    def test_http_api_accepts_valid_bearer_token(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            app.db_connect().close()
            server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            url = f"http://127.0.0.1:{server.server_port}/api/reservations/next"
            req = request.Request(url, method="POST", headers={"Authorization": "Bearer secret"})
            try:
                with mock.patch.object(app, "paperless_max_asn", return_value=9):
                    with request.urlopen(req, timeout=3) as resp:
                        self.assertEqual(resp.status, 200)
                        self.assertIn(b"ASN00010", resp.read())
            finally:
                server.shutdown()
                server.server_close()

    def test_http_peek_requires_and_uses_auth(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            app.db_connect().close()
            server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            url = f"http://127.0.0.1:{server.server_port}/api/reservations/next"
            try:
                with self.assertRaises(HTTPError) as ctx:
                    request.urlopen(request.Request(url, method="GET"), timeout=3)
                self.assertEqual(ctx.exception.code, 401)

                req = request.Request(url, method="GET", headers={"Authorization": "Bearer secret"})
                with mock.patch.object(app, "paperless_max_asn", return_value=6):
                    with request.urlopen(req, timeout=3) as resp:
                        self.assertEqual(resp.status, 200)
                        self.assertIn(b"ASN00007", resp.read())
            finally:
                server.shutdown()
                server.server_close()

    def test_http_reset_requires_and_uses_auth(self):
        with tempfile.TemporaryDirectory() as temp:
            app = load_app(os.path.join(temp, "db.sqlite3"))
            app.db_connect().close()
            server = ThreadingHTTPServer(("127.0.0.1", 0), app.Handler)
            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            url = f"http://127.0.0.1:{server.server_port}/api/admin/reset-to-paperless"
            try:
                with self.assertRaises(HTTPError) as ctx:
                    request.urlopen(request.Request(url, method="POST"), timeout=3)
                self.assertEqual(ctx.exception.code, 401)

                req = request.Request(url, method="POST", headers={"Authorization": "Bearer secret"})
                with mock.patch.object(app, "paperless_max_asn", return_value=12):
                    with request.urlopen(req, timeout=3) as resp:
                        self.assertEqual(resp.status, 200)
                        self.assertIn(b"ASN00013", resp.read())
            finally:
                server.shutdown()
                server.server_close()


if __name__ == "__main__":
    unittest.main()
