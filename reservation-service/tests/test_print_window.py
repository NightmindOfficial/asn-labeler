import os
import tempfile
import unittest
from unittest import mock
from test_app import load_app


class PrintWindowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        self.app.CONFIG["printing_settle"] = 120
        self.patches = [mock.patch.object(self.app, "now", return_value=1000),
                        mock.patch.object(self.app, "paperless_max_asn", return_value=0),
                        mock.patch.object(self.app, "paperless_documents", return_value={"results": []}),
                        mock.patch.object(self.app, "paperless_for_rows", return_value={})]
        self.clock = self.patches[0].start()
        self.addCleanup(self.patches[0].stop)
        for patch in self.patches[1:]:
            patch.start()
            self.addCleanup(patch.stop)
        self.rid = self.app.reserve_next()["reservationId"]
        self.app.transition(self.rid, "printing")

    def test_in_print_until_exact_deadline_then_uncertain(self):
        for timestamp, state, inflight in ((1000,"in_print",1), (1119,"in_print",1), (1120,"uncertain",0)):
            self.clock.return_value = timestamp
            data = self.app.document_status()
            self.assertEqual(data["items"][0]["status"], state)
            self.assertEqual(data["inPrint"], inflight)
            self.assertEqual(data["uncertain"], 1-inflight)
            self.assertEqual(data["openCount"], 1)
            self.assertEqual(self.app.admin_state()["items"][0]["displayStatus"], state)
            self.assertEqual(self.app.peek_next()["asnNumber"], 2)

    def test_both_resolution_paths_rejected_during_print(self):
        self.clock.return_value = 1119
        for exists in (True, False):
            with self.assertRaisesRegex(ValueError, "Print may"):
                self.app.resolve_uncertain(self.rid, exists, physical_label_absent=True)
        self.assertEqual(self.app.admin_state()["items"][0]["status"], "printing")
        self.clock.return_value = 1120
        self.assertEqual(self.app.resolve_uncertain(self.rid, True)["status"], "pending")

    def test_success_before_deadline_and_late_success_are_accepted(self):
        for timestamp in (1001, 1150):
            with self.subTest(timestamp=timestamp):
                self.clock.return_value = timestamp
                self.app.transition(self.rid, "printed")
                data = self.app.document_status()
                self.assertEqual(data["items"][0]["status"], "pending")
                self.assertEqual(data["inPrint"], 0)
                self.assertEqual(data["uncertain"], 0)

    def test_timeout_never_releases_and_late_ack_still_works(self):
        self.clock.return_value = 1200
        self.assertEqual(self.app.document_status()["items"][0]["status"], "uncertain")
        self.assertEqual(self.app.reserve_next()["asnNumber"], 2)
        self.app.transition(self.rid, "printed")
        self.assertEqual(next(i for i in self.app.document_status()["items"] if i["reservationId"] == self.rid)["status"], "pending")

    def test_short_configured_windows_keep_active_asn_protected(self):
        for seconds in (10, 30):
            with self.subTest(seconds=seconds):
                self.app.CONFIG["printing_settle"] = seconds
                self.clock.return_value = 1000 + seconds - 1
                self.assertEqual(self.app.document_status()["items"][0]["status"], "in_print")
                self.clock.return_value += 1
                self.assertEqual(self.app.document_status()["items"][0]["status"], "uncertain")
                self.assertEqual(self.app.peek_next()["asnNumber"], 2)

    def test_printing_settle_configuration_bounds(self):
        self.app.CONFIG.update(api_keys={"c" * 32}, admin_keys={"a" * 32})
        for seconds in (10, 30, 120, 86400):
            self.app.CONFIG["printing_settle"] = seconds
            self.app.validate_config()
        for seconds in (0, 9, 86401):
            self.app.CONFIG["printing_settle"] = seconds
            with self.assertRaisesRegex(RuntimeError, "settle"):
                self.app.validate_config()
