import io
import json
import os
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from unittest import mock
from test_app import load_app


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        self.app.CONFIG["admin_keys"] = {"admin-key"}
        self.app.db_connect().close()
        patch = mock.patch.object(self.app, "paperless_max_asn", return_value=2)
        self.remote = patch.start()
        self.addCleanup(patch.stop)
        self.seed(2, "printed")
        self.seed(3, "blocked")
        self.seed(4, "reusable")

    def seed(self, number, state):
        with self.app.transaction() as conn:
            conn.execute("INSERT OR REPLACE INTO reservations VALUES (?,?,?,?,?,?,?,?)",
                         (str(number), number, self.app.barcode_text(number), state,
                          self.app.now(), self.app.now(), self.app.now()+300, "owner"))

    def unblock(self):
        return self.app.unblock_reservation("3", "UNBLOCK ASN", True)

    def test_tail_unblock_reuses_lowest_number_and_audits(self):
        self.assertEqual(self.app.peek_next()["asnNumber"], 4)
        self.assertEqual(self.unblock()["nextAsnNumber"], 3)
        self.assertEqual(self.app.admin_events()["items"][-1]["reason"], "admin_unblocked")
        self.assertEqual(self.app.reserve_next()["asnNumber"], 3)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 4)

    def test_higher_protected_states_reject(self):
        for state in ("reserved", "printing", "printed"):
            self.seed(4, state)
            with self.assertRaises(ValueError): self.unblock()

    def test_remote_equal_higher_or_historical_floor_reject(self):
        for maximum in (3, 4):
            self.remote.return_value = maximum
            with self.assertRaises(ValueError): self.unblock()
        self.remote.return_value = 2
        with self.app.transaction() as conn: self.app.remote_floor(conn, 4)
        with self.assertRaises(ValueError): self.unblock()

    def test_cancel_below_block_and_reuse_before_higher_reusable(self):
        self.remote.return_value = 1
        with mock.patch.object(self.app, "paperless_documents", return_value={"results": []}):
            result = self.app.resolve_uncertain("2", False, cancel=True, physical_label_absent=True)
        self.assertEqual(result["status"], "next")
        self.assertEqual(self.app.reserve_next()["asnNumber"], 2)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 4)
        self.assertEqual(next(r for r in self.app.admin_state()["items"] if r["id"] == "3")["status"], "blocked")

    def test_higher_block_does_not_prevent_explicit_unblock(self):
        self.seed(4, "blocked")
        self.assertEqual(self.unblock()["nextAsnNumber"], 3)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 3)
        self.assertEqual(self.app.reserve_next()["asnNumber"], 5)

    def test_reported_case_unblock_then_cancel_uses_two_three_four(self):
        self.remote.return_value = 1
        self.assertEqual(self.unblock()["nextAsnNumber"], 3)
        with mock.patch.object(self.app, "paperless_documents", return_value={"results": []}):
            self.assertEqual(self.app.resolve_uncertain("2", False, cancel=True,
                physical_label_absent=True)["status"], "next")
        self.assertEqual([self.app.reserve_next()["asnNumber"] for _ in range(3)], [2, 3, 4])

    def test_explicit_confirmation_strict_boolean_and_replay(self):
        for phrase, absent in (("", True), ("UNBLOCK ASN", False), ("UNBLOCK ASN", "true")):
            with self.assertRaises(ValueError): self.app.unblock_reservation("3", phrase, absent)
        self.unblock()
        with self.assertRaises(ValueError): self.unblock()

    def test_paperless_offline_does_not_change_state(self):
        self.remote.side_effect = RuntimeError("offline")
        with self.assertRaises(RuntimeError): self.unblock()
        self.assertEqual(next(r for r in self.app.admin_state()["items"] if r["id"] == "3")["status"], "blocked")

    def test_concurrent_allocation_and_unblock_never_duplicate(self):
        def attempt():
            try: return self.unblock()
            except ValueError: return None
        with ThreadPoolExecutor(max_workers=3) as pool:
            released = pool.submit(attempt)
            allocations = [pool.submit(self.app.reserve_next) for _ in range(2)]
            released.result()
            numbers = [a.result()["asnNumber"] for a in allocations]
        self.assertEqual(len(set(numbers)), 2)
        self.assertIn(sorted(numbers), ([3, 4], [4, 5]))

    def test_status_sorted_numerically_with_titles(self):
        self.seed(10, "printed")
        with mock.patch.object(self.app, "paperless_for_rows", return_value={2: {"title": "Invoice"}}):
            data = self.app.document_status()
        self.assertEqual([r["asnNumber"] for r in data["items"]], [10, 3, 2])
        self.assertEqual(data["items"][-1]["documentTitle"], "Invoice")

    def test_route_admin_only_and_noop_preview(self):
        def call(path, key, body):
            encoded = json.dumps(body).encode()
            req = self.app.ApiRequest("POST", path, {"Authorization": "Bearer "+key,
                "Content-Length": str(len(encoded))}, io.BytesIO(encoded), "local")
            req.dispatch("POST")
            return req.response[0], json.loads(req.response[2])
        body = {"confirmation": "UNBLOCK ASN", "physicalLabelAbsent": True}
        path = "/api/admin/reservations/3/unblock"
        self.assertEqual(call(path, "secret", body)[0], 401)
        self.assertEqual(call(path, "admin-key", body)[0], 200)
        status, preview = call("/api/admin/high-water-preview", "admin-key", {})
        self.assertEqual(status, 200)
        self.assertFalse(preview["needsCorrection"])
