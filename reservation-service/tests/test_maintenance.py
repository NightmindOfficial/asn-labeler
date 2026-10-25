import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

from test_app import load_app


class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.app = load_app(os.path.join(self.folder.name, "db"))
        spec = importlib.util.spec_from_file_location("maintenance_test", Path(__file__).parents[1] / "maintenance.py")
        self.maintenance = importlib.util.module_from_spec(spec)
        with mock.patch.dict("sys.modules", {"app": self.app}):
            spec.loader.exec_module(self.maintenance)

    def test_prepare_backup_lease_and_resume(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            pending = self.app.reserve_next()
            result = self.maintenance.prepare("2.2.0")
            self.assertTrue((Path(self.folder.name) / "backups" / result["backup"]).is_file())
            with self.assertRaises(self.app.AdmissionBlocked):
                self.app.reserve_next()
            statuses = []
            response = self.app.wsgi_app({"REQUEST_METHOD": "POST", "PATH_INFO": "/api/reservations/next",
                "wsgi.input": io.BytesIO(), "HTTP_AUTHORIZATION": "Bearer secret",
                "HTTP_X_ASN_CLIENT_VERSION": "2.1.1", "HTTP_X_ASN_API_VERSION": "1"},
                lambda status, headers: statuses.append(status))
            self.assertTrue(statuses[0].startswith("503"))
            self.assertEqual(json.loads(response[0])["errorCode"], "service_maintenance")
            with self.assertRaises(self.app.AdmissionBlocked):
                self.app.transition(pending["reservationId"], "printing")
            self.app.transition(pending["reservationId"], "released")
            self.assertFalse(self.maintenance.resume(result["lease"] + 1)["resumed"])
            with self.assertRaises(ValueError):
                self.maintenance.prepare("2.2.0")
            self.assertTrue(self.maintenance.resume(result["lease"])["resumed"])
            self.app.reserve_next()

    def test_unresolved_job_prevents_update_even_after_settle(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            pending = self.app.reserve_next()
        self.app.transition(pending["reservationId"], "printing")
        with mock.patch.object(self.app, "now", return_value=9999999999):
            with self.assertRaises(ValueError):
                self.maintenance.prepare("2.2.0")
        self.app.transition(pending["reservationId"], "printed")
        self.assertTrue(self.maintenance.prepare("2.2.0")["ready"])

    def test_secondary_pending_and_downgrade_rejected(self):
        with self.assertRaises(ValueError):
            self.maintenance.prepare("2.0.0")
        with self.app.transaction() as conn:
            conn.execute("INSERT INTO secondary_labels VALUES ('copy',1,1,'test',1,NULL)")
        with self.assertRaises(ValueError):
            self.maintenance.prepare("2.2.0")
