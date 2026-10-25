import io
import json
import os
import tempfile
import unittest
import uuid
from contextlib import closing
from concurrent.futures import ThreadPoolExecutor
from unittest import mock
from test_app import load_app


class CopyLabelTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))

    def test_list_uses_only_paperless_and_does_not_reserve(self):
        with mock.patch.object(self.app, "paperless_documents", return_value={"results": [
            {"id": 22, "archive_serial_number": 2, "title": "Title"},
            {"id": 99, "archive_serial_number": 9, "title": "Other"}], "next": "page2"}) as fetch:
            result = self.app.copy_label_documents(2)
        self.assertEqual([item["asnNumber"] for item in result["items"]], [9, 2])
        self.assertEqual(result["items"][0]["barcodeText"], "ASN00009")
        self.assertTrue(result["hasMore"])
        self.assertEqual(fetch.call_args.args[0]["archive_serial_number__gt"], 0)
        self.assertEqual(fetch.call_args.args[0]["page"], 2)
        with closing(self.app.db_connect()) as conn:
            self.assertEqual(conn.execute("SELECT COUNT(*) FROM reservations").fetchone()[0], 0)

    def test_revalidation_missing_changed_duplicate_or_invalid_rejected(self):
        for rows in ([], [{"id": 1, "archive_serial_number": 3}],
                     [{"id": 1, "archive_serial_number": 2}, {"id": 2, "archive_serial_number": 2}],
                     [{"id": 1, "archive_serial_number": None}], [{"id": None, "archive_serial_number": 2}]):
            with self.subTest(rows=rows), mock.patch.object(self.app, "paperless_documents", return_value={"results": rows}):
                with self.assertRaises((ValueError, self.app.PaperlessError)):
                    self.app.copy_label_documents(asn=2)

    def test_revalidation_preserves_document_identity(self):
        with mock.patch.object(self.app, "paperless_documents", return_value={"results": [{"id": 42, "archive_serial_number": 2, "title": "Doc"}]}) as fetch:
            result = self.app.copy_label_documents(asn=2)
        self.assertEqual(result["items"][0]["documentId"], 42)
        self.assertEqual(fetch.call_args.args[0], {"archive_serial_number": 2, "page_size": 2})

    def test_offline_and_invalid_parameters_fail_closed(self):
        with mock.patch.object(self.app, "paperless_documents", side_effect=self.app.PaperlessError("paperless_unreachable")):
            with self.assertRaises(self.app.PaperlessError): self.app.copy_label_documents()
        for params in ({"page": 0}, {"asn": 0}, {"asn": 2147483648}):
            with self.assertRaises(ValueError): self.app.copy_label_documents(**params)

    def test_hide_imported_keeps_blocked_and_legacy_filter_unchanged(self):
        with self.app.transaction() as conn:
            for number, state in ((1, "printed"), (2, "blocked"), (3, "printed")):
                conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)", (str(number), number, self.app.barcode_text(number), state, 1, 1, 0, "owner"))
        with mock.patch.object(self.app, "paperless_max_asn", return_value=1), mock.patch.object(self.app, "paperless_for_rows", return_value={1: {"archive_serial_number": 1, "title": "Imported"}}):
            data = self.app.document_status(hide_imported=True)
            self.assertEqual([item["asnNumber"] for item in data["items"]], [3, 2])
            self.assertEqual(data["total"], 2)
            self.assertEqual([item["asnNumber"] for item in self.app.document_status(include_completed=False)["items"]], [3])

    def test_copy_api_requires_authentication(self):
        request = self.app.ApiRequest("GET", "/api/documents/copy-labels", {}, io.BytesIO(), "127.0.0.1")
        # The HTTP/WSGI adapters share dispatch and authentication.
        request.dispatch("GET")
        self.assertEqual(request.response[0], 401)

    def prepare(self, identifier=None):
        body = {"printId": identifier or uuid.uuid4().hex, "documentId": 42, "asnNumber": 2}
        with mock.patch.object(self.app, "paperless_documents", return_value={"results": [{"id": 42, "archive_serial_number": 2}]}):
            self.app.secondary_label(body)
        return body

    def test_only_confirmed_copies_count_and_retries_are_idempotent(self):
        body = self.prepare()
        self.app.secondary_label(body)  # Prepare retry does not need Paperless.
        self.assertEqual(self.app.secondary_counts([{"id": 42}]), {})
        for _ in range(3):
            self.app.secondary_label(body, confirm=True)
        self.assertEqual(self.app.secondary_counts([{"id": 42}]), {(42, 2): 1})
        with closing(self.app.db_connect()) as conn:
            self.assertEqual(conn.execute("SELECT COUNT(*) FROM audit_events WHERE reason='secondary_label_confirmed'").fetchone()[0], 1)
            self.assertEqual(conn.execute("SELECT COUNT(*) FROM reservations").fetchone()[0], 0)
            self.assertEqual(conn.execute("SELECT value FROM metadata WHERE key='paperless_high_water'").fetchone()[0], 0)

    def test_parallel_confirmations_count_each_print_once(self):
        bodies = [self.prepare() for _ in range(8)]
        with ThreadPoolExecutor(max_workers=4) as pool:
            list(pool.map(lambda body: self.app.secondary_label(body, confirm=True), bodies * 3))
        self.assertEqual(self.app.secondary_counts([{"id": 42}]), {(42, 2): 8})

    def test_document_reassignment_does_not_inherit_count(self):
        body = self.prepare()
        self.app.secondary_label(body, confirm=True)
        for identifier, asn, expected in ((42, 2, 1), (99, 2, 0), (42, 3, 0)):
            with mock.patch.object(self.app, "paperless_documents", return_value={"results": [{"id": identifier, "archive_serial_number": asn}]}):
                self.assertEqual(self.app.copy_label_documents(asn=asn)["items"][0]["secondaryLabelCount"], expected)

    def test_prepare_rejects_changed_document_and_offline(self):
        body = {"printId": uuid.uuid4().hex, "documentId": 42, "asnNumber": 2}
        with mock.patch.object(self.app, "paperless_documents", return_value={"results": [{"id": 99, "archive_serial_number": 2}]}):
            with self.assertRaises(ValueError): self.app.secondary_label(body)
        with mock.patch.object(self.app, "paperless_documents", side_effect=self.app.PaperlessError("paperless_unreachable")):
            with self.assertRaises(self.app.PaperlessError): self.app.secondary_label(body)
        with self.assertRaises(KeyError): self.app.secondary_label(body, confirm=True)

    def test_owner_and_id_identity_are_protected(self):
        body = self.prepare()
        with self.assertRaises(ValueError): self.app.secondary_label({**body, "documentId": 43})
        self.app.OWNER.set("another-client")
        with self.assertRaises(PermissionError): self.app.secondary_label(body, confirm=True)

    def test_document_list_exposes_count_without_new_status(self):
        body = self.prepare()
        self.app.secondary_label(body, confirm=True)
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)", ("two", 2, "ASN00002", "printed", 1, 1, 0, ""))
        with mock.patch.object(self.app, "paperless_max_asn", return_value=2), mock.patch.object(self.app, "paperless_for_rows", return_value={2: {"id": 42, "title": "Doc"}}):
            item = self.app.document_status()["items"][0]
        self.assertEqual((item["status"], item["secondaryLabelCount"]), ("imported", 1))

    def test_schema_two_migration_preserves_reservations(self):
        with closing(self.app.db_connect()) as conn:
            conn.execute("DROP TABLE secondary_labels")
            conn.execute("PRAGMA user_version=2")
            conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)", ("two", 2, "ASN00002", "blocked", 1, 1, 0, ""))
        self.app.backup_before_migration()
        with closing(self.app.db_connect()) as conn:
            self.assertEqual(conn.execute("PRAGMA user_version").fetchone()[0], 3)
            self.assertEqual(conn.execute("SELECT status FROM reservations").fetchone()[0], "blocked")

    def test_api_compatibility_and_copy_auth(self):
        def call(path, headers=None, method="GET", body=None):
            data = json.dumps(body or {}).encode()
            request = self.app.ApiRequest(method, path, {"Content-Length": str(len(data)), **(headers or {})}, io.BytesIO(data), "127.0.0.1")
            request.dispatch(method)
            return request.response[0]
        self.assertEqual(call("/health"), 200)
        for action in ("start", "printed"):
            self.assertEqual(call("/api/documents/copy-labels/" + action, method="POST"), 401)
        headers = {"Authorization": "Bearer secret", "X-ASN-API-Version": "2"}
        self.assertEqual(call("/api/reservations/next", headers, "POST"), 409)
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            self.assertEqual(call("/api/reservations/next", {"Authorization": "Bearer secret"}), 426)
            self.assertEqual(call("/api/reservations/next", {"Authorization": "Bearer secret", "X-ASN-Client-Version": "2.1.1", "X-ASN-API-Version": "1"}), 200)
