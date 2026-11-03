import os
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from unittest import mock
from test_app import load_app


class HistoryCleanupTests(unittest.TestCase):
    def test_cleanup_backs_up_old_records_preserves_real_events_and_origin(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "db.sqlite3")
            app = load_app(path)
            with closing(app.db_connect()) as conn:
                conn.execute("DELETE FROM metadata WHERE key='audit_noop_cleanup_231'")
                for number in (120, 121):
                    conn.execute("INSERT INTO reservations VALUES (?,?,?,?,?,?,?,?)", (str(number), number, app.barcode_text(number), "printed", 1, 1, 0, "owner"))
                events = [
                    (120, "", "reserved", "allocated"),
                    (120, "reserved", "printing", "client_printing"),
                    (120, "printing", "printed", "client_printed"),
                    (120, "printed", "printed", "paperless_seen_printed"),
                    (121, "", "printed", "paperless_external_import"),
                    (121, "printed", "printed", "paperless_seen_imported"),
                    (121, "copy_prepared", "copy_printed", "secondary_label_confirmed"),
                ]
                for number, old, new, reason in events:
                    conn.execute("INSERT INTO audit_events(time,request_id,actor,reservation_id,asn_number,old_status,new_status,reason) VALUES (1,'test','owner',?,?,?,?,?)", (str(number), number, old, new, reason))
                expected = [tuple(row) for row in conn.execute("SELECT * FROM audit_events WHERE old_status!=new_status ORDER BY seq")]
            app.backup_before_migration()
            with closing(app.db_connect()) as conn:
                self.assertEqual(conn.execute("PRAGMA user_version").fetchone()[0], 3)
                self.assertEqual([tuple(row) for row in conn.execute("SELECT * FROM audit_events ORDER BY seq")], expected)
                self.assertIsNone(conn.execute("SELECT 1 FROM sqlite_master WHERE name='paperless_observations'").fetchone())
            statuses = {r["asn_number"]: r["displayStatus"] for r in app.admin_state()["items"]}
            self.assertEqual(statuses, {120: "printed", 121: "external_imported"})
            with mock.patch.object(app, "paperless_max_asn", return_value=119), mock.patch.object(app, "paperless_for_rows", return_value={}):
                self.assertEqual([r["status"] for r in app.document_status()["items"]], ["pending", "pending"])
            self.assertEqual([tuple(r.values()) for r in app.admin_events()["items"]], expected)
            app.backup_before_migration()
            backups = list((Path(directory) / "backups").glob("*.sqlite3"))
            self.assertEqual(len(backups), 1)
            with closing(sqlite3.connect(backups[0])) as conn:
                self.assertEqual(conn.execute("SELECT COUNT(*) FROM audit_events").fetchone()[0], 7)

    def test_new_noop_events_are_not_written(self):
        with tempfile.TemporaryDirectory() as directory:
            app = load_app(os.path.join(directory, "db.sqlite3"))
            with app.transaction() as conn:
                app.audit(conn, {"id": "test", "asn_number": 120, "status": "printed"}, "printed", "label_exists")
                self.assertEqual(conn.execute("SELECT COUNT(*) FROM audit_events").fetchone()[0], 0)
            self.assertEqual(app.admin_events()["items"], [])
            self.assertEqual(app.recent_admin_events()["items"], [])
