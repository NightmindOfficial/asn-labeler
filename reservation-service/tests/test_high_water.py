import io
import json
import os
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from unittest import mock
from test_app import load_app


class HighWaterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        self.app.CONFIG["admin_keys"] = {"admin-key"}
        with self.app.transaction() as conn:
            self.app.remote_floor(conn, 2)
        patch = mock.patch.object(self.app, "paperless_max_asn", return_value=1)
        self.remote = patch.start()
        self.addCleanup(patch.stop)

    def correct(self, preview):
        return self.app.correct_high_water(preview["previewToken"], "LOWER HIGH WATER", True)

    def test_deleted_unlabelled_tail_can_be_reused_and_is_audited(self):
        preview = self.app.high_water_preview()
        self.assertEqual(preview["nextAsnNumber"], 2)
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 2)
        result = self.correct(preview)
        self.assertEqual(result["paperlessHighWater"], 1)
        event = self.app.admin_events()["items"][-1]
        self.assertEqual(event["reason"], "admin_high_water_corrected")
        self.assertEqual(event["old_status"], "Hoechststand 2")
        self.assertEqual(self.app.reserve_next()["asnNumber"], 2)

    def test_all_protected_local_states_remain_unchanged(self):
        for state in ("reserved", "printing", "printed", "blocked"):
            with self.subTest(state=state):
                with self.app.transaction() as conn:
                    conn.execute("DELETE FROM reservations")
                    self.app.remote_floor(conn, 2)
                    conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)",
                        ("local", 2, "ASN00002", state, 1, 1, self.app.now()+300, "owner"))
                before = self.app.admin_state()["items"]
                preview = self.app.high_water_preview()
                self.assertEqual(preview["protectedReservations"], 1)
                self.assertEqual(preview["nextAsnNumber"], 3)
                self.assertEqual(self.correct(preview)["nextAsnNumber"], 3)
                self.assertEqual(self.app.admin_state()["items"], before)

    def test_empty_paperless_can_reset_to_zero(self):
        self.remote.return_value = 0
        self.assertEqual(self.correct(self.app.high_water_preview())["nextAsnNumber"], 1)

    def test_confirmation_and_boolean_are_required(self):
        preview = self.app.high_water_preview()
        for phrase, absent in (("", True), ("LOWER HIGH WATER", False), ("LOWER HIGH WATER", "true")):
            with self.assertRaises(ValueError):
                self.app.correct_high_water(preview["previewToken"], phrase, absent)
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 2)

    def test_changed_remote_or_local_high_water_rejects_preview(self):
        preview = self.app.high_water_preview()
        self.remote.return_value = 0
        with self.assertRaises(ValueError): self.correct(preview)
        self.remote.return_value = 1
        with self.app.transaction() as conn: self.app.remote_floor(conn, 3)
        with self.assertRaises(ValueError): self.correct(preview)

    def test_reservation_between_confirmations_rejects_preview(self):
        preview = self.app.high_water_preview()
        self.app.reserve_next()
        with self.assertRaises(ValueError): self.correct(preview)
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 2)

    def test_offline_never_changes_high_water(self):
        preview = self.app.high_water_preview()
        self.remote.side_effect = self.app.PaperlessError("paperless_unreachable")
        with self.assertRaises(self.app.PaperlessError): self.correct(preview)
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 2)

    def test_preview_expiry_owner_and_replay(self):
        preview = self.app.high_water_preview()
        with mock.patch.object(self.app, "now", return_value=self.app.now()+121):
            with self.assertRaises(ValueError): self.correct(preview)
        owner = self.app.OWNER.set("another-admin")
        try:
            with self.assertRaises(ValueError): self.correct(preview)
        finally:
            self.app.OWNER.reset(owner)
        self.correct(preview)
        with self.assertRaises(ValueError): self.correct(preview)

    def test_archive_tokens_are_not_interchangeable(self):
        correction = self.app.high_water_preview()
        archive = self.app.archive_preview()
        with self.assertRaises(ValueError): self.correct(archive)
        with self.assertRaises(ValueError): self.app.archive_open(correction["previewToken"], "BLOCK OUTSTANDING")
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 2)

    def test_two_admins_cannot_apply_stale_corrections(self):
        previews = [self.app.high_water_preview(), self.app.high_water_preview()]
        def apply(preview):
            try: self.correct(preview); return True
            except ValueError: return False
        with ThreadPoolExecutor(2) as pool:
            self.assertEqual(sorted(pool.map(apply, previews)), [False, True])

    def test_admin_only_routes_and_success(self):
        def call(path, key, body):
            data = json.dumps(body).encode()
            req = self.app.ApiRequest("POST", path, {"Authorization":"Bearer "+key,
                "Content-Length":str(len(data))}, io.BytesIO(data), "localhost")
            req.dispatch("POST")
            return req.response[0], json.loads(req.response[2])
        for path in ("/api/admin/high-water-preview", "/api/admin/high-water-correct"):
            self.assertEqual(call(path, "secret", {})[0], 401)
        status, preview = call("/api/admin/high-water-preview", "admin-key", {})
        self.assertEqual(status, 200)
        status, result = call("/api/admin/high-water-correct", "admin-key", {
            "previewToken":preview["previewToken"], "confirmation":"LOWER HIGH WATER", "physicalLabelsAbsent":True})
        self.assertEqual(status, 200)
        self.assertEqual(result["nextAsnNumber"], 2)

    def test_no_lowering_when_remote_is_equal_or_higher(self):
        for maximum in (2, 3):
            self.remote.return_value = maximum
            result = self.app.high_water_preview()
            self.assertFalse(result["needsCorrection"])
            self.assertNotIn("previewToken", result)
