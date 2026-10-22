import os
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from unittest import mock
from test_app import load_app


class ImportReconciliationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)",
                         ("seventeen", 17, "ASN00017", "blocked", 1, 1, 0, "owner"))
        self.docs = {17: {"id": 1, "archive_serial_number": 17, "title": "Found document"}}
        for patch in (mock.patch.object(self.app, "paperless_max_asn", return_value=18),
                      mock.patch.object(self.app, "paperless_for_rows", side_effect=lambda rows: self.docs)):
            patch.start()
            self.addCleanup(patch.stop)

    def test_document_and_reservation_agree_and_audit_once(self):
        result = self.app.document_status()
        self.assertEqual(result["items"][0]["status"], "imported")
        self.assertEqual(result["items"][0]["documentTitle"], "Found document")
        self.assertEqual(self.app.admin_state()["items"][0]["status"], "printed")
        self.app.document_status()
        events = self.app.admin_events()["items"]
        events = [event for event in events if event["reason"] == "paperless_import_confirmed"]
        self.assertEqual(len(events), 1)
        self.assertEqual((events[0]["old_status"], events[0]["new_status"]), ("blocked", "printed"))

    def test_admin_alone_reconciles_without_visiting_documents(self):
        result = self.app.admin_state(refresh_imports=True)
        self.assertEqual(result["items"][0]["status"], "printed")
        self.assertEqual(result["paperlessHighWater"], 17)

    def test_higher_asn_or_mismatching_document_never_unblocks(self):
        for docs in ({}, {18: {"archive_serial_number": 18}}, {17: {"archive_serial_number": 18}}):
            self.docs = docs
            self.assertEqual(self.app.admin_state(refresh_imports=True)["items"][0]["status"], "blocked")

    def test_paperless_deletion_does_not_undo_printed(self):
        self.app.document_status()
        self.docs = {}
        self.assertEqual(self.app.document_status()["items"][0]["status"], "pending")
        self.assertEqual(self.app.admin_state()["items"][0]["status"], "printed")

    def test_offline_keeps_admin_available_and_block_intact(self):
        with mock.patch.object(self.app, "paperless_for_rows", side_effect=self.app.PaperlessError("paperless_unreachable")):
            state = self.app.admin_state(refresh_imports=True)
            self.assertEqual(state["paperlessSyncError"], "paperless_unreachable")
            self.assertEqual(state["items"][0]["status"], "blocked")

    def test_racing_refreshes_emit_only_one_transition(self):
        with ThreadPoolExecutor(max_workers=4) as pool:
            list(pool.map(lambda _: self.app.admin_state(refresh_imports=True), range(4)))
        events = [e for e in self.app.admin_events()["items"] if e["reason"] == "paperless_import_confirmed"]
        self.assertEqual(len(events), 1)

    def test_stale_snapshot_never_overwrites_active_reservation(self):
        rows = self.app.admin_state()["items"]
        with self.app.transaction() as conn:
            conn.execute("UPDATE reservations SET id='new-id',status='reserved' WHERE asn_number=17")
        self.app.reconcile_blocked_imports(rows, self.docs)
        self.assertEqual(self.app.admin_state()["items"][0]["status"], "reserved")
