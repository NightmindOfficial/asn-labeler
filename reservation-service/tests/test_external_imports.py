import os
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from unittest import mock

from test_app import load_app


class ExternalImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        self.docs = {}
        for patch in (
            mock.patch.object(self.app, "paperless_max_asn", side_effect=lambda: max(self.docs, default=0)),
            mock.patch.object(self.app, "paperless_documents", side_effect=self.fetch),
        ):
            patch.start()
            self.addCleanup(patch.stop)
        self.requests = []

    def fetch(self, params):
        self.requests.append(params)
        numbers = sorted(n for n in self.docs
                         if params["archive_serial_number__gt"] < n <= params["archive_serial_number__lte"])
        return {"results": [self.docs[n] for n in numbers[:100]],
                "next": "http://untrusted.invalid/" if len(numbers) > 100 else None}

    def add_doc(self, number):
        self.docs[number] = {"id": number + 1000, "archive_serial_number": number, "title": "Document " + str(number)}

    def add_row(self, number, state):
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)",
                         (str(number), number, self.app.barcode_text(number), state, 1, 1, 0, "owner"))

    def test_production_gap_and_secondary_label(self):
        for number in (119, 121, 122):
            self.add_row(number, "printed")
        self.add_doc(120)
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO secondary_labels VALUES (?,?,?,?,?,?)", ("copy", 1120, 120, "owner", 1, 2))
        result = self.app.document_status()
        self.assertEqual([r["asnNumber"] for r in result["items"]], [122, 121, 120, 119])
        item = result["items"][2]
        self.assertEqual((item["status"], item["documentTitle"], item["secondaryLabelCount"]),
                         ("imported", "Document 120", 1))
        self.assertEqual(result["nextAsnNumber"], 123)
        self.assertEqual(self.app.document_status()["items"][2]["reservationId"], item["reservationId"])

    def test_empty_history_scans_all_pages_without_inventing_gaps(self):
        for number in range(2, 224, 2):
            self.add_doc(number)
        result = self.app.document_status()
        self.assertEqual((result["total"], result["imported"], result["openCount"]), (111, 111, 0))
        self.assertEqual(self.requests[1]["archive_serial_number__gt"], 200)
        self.assertEqual([r["asnNumber"] for r in self.app.document_status(12)["items"]], [2])
        self.assertEqual(self.app.document_status(hide_imported=True)["total"], 0)
        self.assertEqual(self.app.document_status(include_completed=False)["total"], 0)

    def test_imports_below_and_above_history_and_late_gap(self):
        self.add_row(119, "printed")
        for number in (1, 120, 130):
            self.add_doc(number)
        self.assertEqual(self.app.document_status()["total"], 4)
        self.add_doc(50)
        self.assertEqual(self.app.document_status()["total"], 5)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 131)

    def test_reusable_import_is_preserved_after_remote_deletion(self):
        self.add_row(120, "reusable")
        self.add_doc(120)
        self.assertEqual(self.app.document_status()["items"][0]["status"], "imported")
        self.docs.clear()
        result = self.app.document_status()
        self.assertEqual(result["items"][0]["status"], "blocked")
        self.assertEqual(result["nextAsnNumber"], 121)

    def test_active_prints_keep_identity_owner_and_state(self):
        for number, state in ((119, "reserved"), (120, "printing")):
            self.add_row(number, state)
            self.add_doc(number)
        self.assertEqual(self.app.document_status()["imported"], 2)
        with closing(self.app.db_connect()) as conn:
            rows = conn.execute("SELECT id, owner, status FROM reservations ORDER BY asn_number").fetchall()
        self.assertEqual([tuple(r) for r in rows], [("119", "owner", "reserved"), ("120", "owner", "printing")])

    def test_concurrent_refresh_imports_and_audits_once(self):
        self.add_doc(120)
        with ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(lambda _: self.app.document_status(), range(4)))
        self.assertTrue(all(r["total"] == 1 for r in results))
        events = [e for e in self.app.admin_events()["items"] if e["reason"] == "paperless_external_import"]
        self.assertEqual(len(events), 1)

    def test_partial_fetch_failure_does_not_materialize_documents(self):
        self.add_doc(120)
        with mock.patch.object(self.app, "paperless_documents", side_effect=[
            {"results": [{"id": 1, "archive_serial_number": 1}], "next": "next"},
            self.app.PaperlessError("paperless_unreachable"),
        ]):
            with self.assertRaises(self.app.PaperlessError):
                self.app.document_status()
        with closing(self.app.db_connect()) as conn:
            self.assertEqual(conn.execute("SELECT COUNT(*) FROM reservations").fetchone()[0], 0)


    def local_status(self, number=120):
        rows = self.app.admin_state()["items"]
        return next(row["displayStatus"] for row in rows if row["asn_number"] == number)

    def test_external_import_disappearance_and_return_keep_origin_across_restart(self):
        self.add_doc(120)
        self.assertEqual(self.app.admin_state(refresh_imports=True)["items"][0]["displayStatus"], "external_imported")
        self.assertEqual(self.app.admin_state(hide_printed=True)["total"], 1)
        self.docs.clear()
        self.assertEqual(self.app.admin_state(refresh_imports=True)["items"][0]["displayStatus"], "blocked")
        self.assertEqual(self.app.document_status()["items"][0]["status"], "blocked")
        # A fresh service instance uses persisted audit provenance, not a cache.
        restarted = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        self.add_doc(120)
        with mock.patch.object(restarted, "paperless_max_asn", return_value=120), mock.patch.object(restarted, "paperless_documents", side_effect=self.fetch):
            self.assertEqual(restarted.document_status()["items"][0]["status"], "imported")
        self.assertEqual(restarted.admin_state()["items"][0]["displayStatus"], "external_imported")

    def test_printed_import_disappearance_and_return_keep_print_history(self):
        self.add_row(120, "printed")
        self.assertEqual(self.app.document_status()["items"][0]["status"], "pending")
        self.add_doc(120)
        self.assertEqual(self.app.document_status()["items"][0]["status"], "imported")
        self.assertEqual(self.local_status(), "printed")
        self.assertEqual(self.app.admin_state(hide_printed=True)["total"], 0)
        self.docs.clear()
        self.assertEqual(self.app.document_status()["items"][0]["status"], "pending")
        self.assertEqual(self.local_status(), "printed")
        self.add_doc(120)
        self.assertEqual(self.app.document_status()["items"][0]["status"], "imported")
        self.assertEqual(self.local_status(), "printed")

    def test_first_import_of_blocked_asns_uses_actual_print_history(self):
        self.add_row(119, "printed")
        self.add_row(120, "blocked")
        with self.app.transaction() as conn:
            row = conn.execute("SELECT * FROM reservations WHERE asn_number=119").fetchone()
            self.app.set_state(conn, row, "blocked", "admin_archive_open")
        for number in (119, 120):
            self.add_doc(number)
        self.app.document_status()
        self.assertEqual(self.local_status(119), "printed")
        self.assertEqual(self.local_status(120), "external_imported")

    def test_secondary_print_does_not_change_import_origin(self):
        self.add_doc(120)
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO secondary_labels VALUES (?,?,?,?,?,?)", ("secondary", 1120, 120, "owner", 1, 2))
        item = self.app.document_status()["items"][0]
        self.assertEqual(item["secondaryLabelCount"], 1)
        self.assertEqual(self.local_status(), "external_imported")

    def test_offline_does_not_block_previously_seen_document(self):
        self.add_doc(120)
        self.app.document_status()
        with mock.patch.object(self.app, "paperless_documents", side_effect=self.app.PaperlessError("paperless_unreachable")):
            with self.assertRaises(self.app.PaperlessError):
                self.app.document_status()
            state = self.app.admin_state(refresh_imports=True)
        self.assertEqual(state["paperlessSyncError"], "paperless_unreachable")
        self.assertEqual(self.local_status(), "external_imported")

    def test_partial_fetch_failure_does_not_block_previously_seen_document(self):
        self.add_doc(120)
        self.app.document_status()
        with mock.patch.object(self.app, "paperless_documents", side_effect=[
            {"results": [{"id": 1, "archive_serial_number": 1}], "next": "next"},
            self.app.PaperlessError("paperless_unreachable"),
        ]):
            with self.assertRaises(self.app.PaperlessError):
                self.app.document_status()
        self.assertEqual(self.local_status(), "external_imported")

    def test_active_print_is_not_blocked_and_acknowledgement_is_preserved(self):
        row = self.app.reserve_next()
        self.app.transition(row["reservationId"], "printing")
        self.add_doc(row["asnNumber"])
        self.app.document_status()
        self.docs.clear()
        self.app.document_status()
        self.assertEqual(self.local_status(row["asnNumber"]), "uncertain")
        self.app.transition(row["reservationId"], "printed")
        self.assertEqual(self.app.document_status()["items"][0]["status"], "pending")
        self.add_doc(row["asnNumber"])
        self.app.document_status()
        self.assertEqual(self.local_status(row["asnNumber"]), "printed")

    def test_manual_release_is_not_undone_by_missing_document_check(self):
        self.add_doc(120)
        self.app.document_status()
        self.docs.clear()
        self.app.document_status()
        row = self.app.admin_state()["items"][0]
        preview = self.app.high_water_preview()
        self.app.correct_high_water(preview["previewToken"], "LOWER HIGH WATER", True)
        self.app.unblock_reservation(row["id"], "UNBLOCK ASN", True)
        self.assertEqual(self.app.document_status()["items"], [])
        self.assertEqual(self.local_status(), "reusable")
        self.assertEqual(self.app.reserve_next()["asnNumber"], 120)

    def test_stale_negative_snapshot_cannot_override_a_new_positive_observation(self):
        self.add_doc(120)
        self.app.document_status()
        snapshots = self.app.admin_state()["items"]
        with closing(self.app.db_connect()) as conn:
            revision = conn.execute("SELECT MAX(seq) FROM audit_events").fetchone()[0]
        self.docs.clear()
        self.app.document_status()
        self.add_doc(120)
        self.app.document_status()
        self.app.reconcile_external_imports({}, snapshots, revision)
        self.assertEqual(self.local_status(), "external_imported")

    def test_missing_document_is_audited_only_once(self):
        self.add_doc(120)
        self.app.document_status()
        self.docs.clear()
        for _ in range(3):
            self.app.document_status()
        events = self.app.admin_events()["items"]
        self.assertEqual(len([e for e in events if e["reason"] == "paperless_document_missing"]), 1)


    def test_primary_print_detection_and_disappearance_add_no_audit_events(self):
        self.add_row(120, "printed")
        before = self.app.admin_events()["items"]
        self.add_doc(120)
        self.app.document_status()
        self.docs.clear()
        for _ in range(3):
            self.assertEqual(self.app.document_status()["items"][0]["status"], "pending")
        self.assertEqual(self.local_status(), "printed")
        self.assertEqual(self.app.admin_events()["items"], before)

    def test_imported_next_asn_is_reusable_without_lowering_historical_floor(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        del self.docs[120]
        result = self.app.document_status()
        self.assertEqual([item["asnNumber"] for item in result["items"]], [119])
        self.assertEqual(result["nextAsnNumber"], 120)
        self.assertEqual(self.local_status(), "reusable")
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 120)
        events = self.app.admin_events()["items"]
        self.assertEqual((events[-1]["old_status"], events[-1]["new_status"]), ("external_imported", "reusable"))
        self.assertEqual(self.app.peek_next()["asnNumber"], 120)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 120)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 121)

    def test_concurrent_allocation_reuses_imported_tail_only_once(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        del self.docs[120]
        self.app.document_status()
        with ThreadPoolExecutor(max_workers=4) as pool:
            numbers = list(pool.map(lambda _: self.app.reserve_next()["asnNumber"], range(4)))
        self.assertEqual(sorted(numbers), [120, 121, 122, 123])

    def test_reuse_exception_persists_across_restart(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        del self.docs[120]
        self.app.document_status()
        restarted = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        with mock.patch.object(restarted, "paperless_max_asn", return_value=119):
            self.assertEqual(restarted.reserve_next()["asnNumber"], 120)

    def test_higher_local_block_prevents_import_reuse(self):
        self.add_doc(119)
        self.add_doc(120)
        self.add_row(121, "blocked")
        self.app.document_status()
        del self.docs[120]
        self.app.document_status()
        self.assertEqual(self.local_status(), "blocked")
        self.assertEqual(self.app.peek_next()["asnNumber"], 122)

    def test_higher_historical_floor_prevents_import_reuse(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        with self.app.transaction() as conn:
            self.app.remote_floor(conn, 125)
        del self.docs[120]
        self.app.document_status()
        self.assertEqual(self.local_status(), "blocked")
        self.assertEqual(self.app.peek_next()["asnNumber"], 126)

    def test_secondary_label_keeps_missing_import_pending_with_counter(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO secondary_labels VALUES (?,?,?,?,?,?)", ("copy", 1120, 120, "owner", 1, 2))
        before = self.app.admin_events()["items"]
        del self.docs[120]
        result = self.app.document_status()
        item = next(r for r in result["items"] if r["asnNumber"] == 120)
        self.assertEqual((item["status"], item["secondaryLabelCount"]), ("pending", 1))
        self.assertEqual(self.local_status(), "external_imported")
        self.assertEqual(self.app.admin_events()["items"], before)
        self.assertEqual(result["nextAsnNumber"], 121)

    def test_secondary_history_alone_protects_missing_import(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        with self.app.transaction() as conn:
            self.app.audit(conn, {"id": "copy", "asn_number": 120, "status": "copy_prepared"}, "copy_printed", "secondary_label_confirmed")
        del self.docs[120]
        item = self.app.document_status()["items"][0]
        self.assertEqual((item["status"], item["secondaryLabelCount"]), ("pending", 1))
        self.assertEqual(self.local_status(), "external_imported")

    def test_unconfirmed_secondary_job_prevents_automatic_reuse(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO secondary_labels VALUES (?,?,?,?,?,?)", ("uncertain", 1120, 120, "owner", 1, None))
        del self.docs[120]
        self.assertEqual(self.app.document_status()["items"][0]["status"], "pending")
        self.assertEqual(self.app.peek_next()["asnNumber"], 121)

    def test_reappearing_import_revokes_reuse_exception(self):
        self.add_doc(119)
        self.add_doc(120)
        self.app.document_status()
        del self.docs[120]
        self.app.document_status()
        self.add_doc(120)
        self.assertEqual(self.app.document_status()["items"][0]["status"], "imported")
        self.assertEqual(self.local_status(), "external_imported")
        self.assertEqual(self.app.peek_next()["asnNumber"], 121)
