import os
import tempfile
import unittest
from test_app import load_app


class AdminListTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))

    def add_events(self, count):
        with self.app.transaction() as conn:
            for i in range(count):
                conn.execute("INSERT INTO audit_events(time,request_id,actor,reservation_id,asn_number,old_status,new_status,reason) VALUES (1,'test','test','test',1,'reserved','printing','test')")

    def test_newest_twenty_then_exclusive_cursor(self):
        self.add_events(45)
        first = self.app.recent_admin_events()
        self.assertEqual([r["seq"] for r in first["items"]], list(range(45,25,-1)))
        self.assertTrue(first["hasMore"])
        self.add_events(3)
        second = self.app.recent_admin_events(first["nextBefore"])
        self.assertEqual([r["seq"] for r in second["items"]], list(range(25,5,-1)))
        third = self.app.recent_admin_events(second["nextBefore"])
        self.assertEqual([r["seq"] for r in third["items"]], list(range(5,0,-1)))
        self.assertFalse(third["hasMore"])
        self.assertIsNone(third["nextBefore"])
        self.assertEqual(self.app.recent_admin_events()["items"][0]["seq"], 48)

    def test_empty_exact_page_and_invalid_cursor(self):
        self.assertEqual(self.app.recent_admin_events()["items"], [])
        self.add_events(20)
        self.assertFalse(self.app.recent_admin_events()["hasMore"])
        for cursor in (0,-1):
            with self.assertRaises(ValueError): self.app.recent_admin_events(cursor)

    def test_existing_incremental_api_stays_compatible(self):
        self.add_events(25)
        self.assertEqual([r["seq"] for r in self.app.admin_events(20)["items"]], list(range(21,26)))

    def test_filter_is_applied_before_pagination_and_count(self):
        with self.app.transaction() as conn:
            for number in range(1,1004):
                conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)",
                             (str(number),number,self.app.barcode_text(number),"printed" if number > 2 else "blocked",1,1,0,"owner"))
        filtered = self.app.admin_state(hide_printed=True)
        self.assertEqual(filtered["total"], 2)
        self.assertEqual([row["asn_number"] for row in filtered["items"]], [2,1])
        self.assertEqual(self.app.admin_state(2, hide_printed=True)["items"], [])
        self.assertEqual(self.app.admin_state()["total"], 1003)
        self.assertEqual(len(self.app.admin_state()["items"]), 1000)


    def test_history_numbers_close_gaps_without_changing_cursors(self):
        self.add_events(477)
        with self.app.transaction() as conn:
            conn.execute("UPDATE sqlite_sequence SET seq=594 WHERE name='audit_events'")
        self.add_events(25)
        first = self.app.recent_admin_events()
        self.assertEqual([r["seq"] for r in first["items"]], list(range(619, 599, -1)))
        self.assertEqual([r["displayNumber"] for r in first["items"]], list(range(502, 482, -1)))
        self.assertEqual(first["nextBefore"], 600)
        # New events do not change numbers on an already displayed older page.
        self.add_events(2)
        second = self.app.recent_admin_events(first["nextBefore"])
        self.assertEqual([r["displayNumber"] for r in second["items"]], list(range(482, 462, -1)))
        pair = [(r["seq"], r["displayNumber"]) for r in second["items"]]
        self.assertIn((595, 478), pair)
        self.assertIn((477, 477), pair)
        self.assertEqual(self.app.recent_admin_events()["items"][0]["displayNumber"], 504)
        self.assertEqual([r["seq"] for r in self.app.admin_events(619)["items"]], [620, 621])

    def test_empty_and_single_event_number_after_cleanup(self):
        self.assertEqual(self.app.recent_admin_events()["items"], [])
        self.add_events(1)
        with self.app.transaction() as conn:
            conn.execute("DELETE FROM audit_events")
        self.add_events(1)
        item = self.app.recent_admin_events()["items"][0]
        self.assertEqual((item["seq"], item["displayNumber"]), (2, 1))

    def test_hide_printed_includes_imports_before_pagination(self):
        with self.app.transaction() as conn:
            for number in range(1, 1004):
                state = "blocked" if number == 1 else "reusable" if number == 2 else "printed"
                conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)", (str(number), number, self.app.barcode_text(number), state, 1, 1, 0, "owner"))
                if number > 2 and number % 2:
                    self.app.audit(conn, {"id": str(number), "asn_number": number, "status": ""}, "external_imported", "paperless_external_import")
        visible = self.app.admin_state(hide_printed=True)
        self.assertEqual(visible["total"], 2)
        self.assertEqual([r["asn_number"] for r in visible["items"]], [2, 1])
        self.assertEqual(self.app.admin_state(2, hide_printed=True)["items"], [])
        self.assertEqual(self.app.admin_state()["total"], 1003)
