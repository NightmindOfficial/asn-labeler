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
