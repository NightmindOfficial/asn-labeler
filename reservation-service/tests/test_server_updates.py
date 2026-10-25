import io
import json
import os
import tempfile
import unittest
from unittest import mock

from test_app import load_app
from server_updates import ServerUpdates, allowed_url, latest_policy


def release_fetch(minimum="2.2.0", latest="2.2.0"):
    def fetch(url):
        if "/releases?" in url:
            return [{"tag_name": "server-v" + latest, "assets": [{"name": "server-update-policy.json",
                "browser_download_url": "https://github.com/NightmindOfficial/asn-labeler/releases/download/server-v" + latest + "/server-update-policy.json"}]}]
        return {"schema": 1, "version": latest, "minimumSafeServerVersion": minimum}
    return fetch


class ServerUpdateTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory()
        self.addCleanup(folder.cleanup)
        self.app = load_app(os.path.join(folder.name, "db"))
        # Model an installed older server, independent of the release under test.
        self.app.VERSION = "2.1.1"
        self.updates = self.app.server_updates()

    def test_unknown_and_network_failure_do_not_block(self):
        self.assertEqual(self.updates.status()["state"], "unknown")
        state = self.updates.check(mock.Mock(side_effect=OSError("offline")))
        self.assertTrue(state["checkFailed"])
        self.assertFalse(state["blocked"])

    def test_block_survives_outage_restart_and_stale_release(self):
        self.assertTrue(self.updates.check(release_fetch())["blocked"])
        self.assertTrue(self.updates.check(mock.Mock(side_effect=OSError()))["blocked"])
        restored = ServerUpdates(self.updates.connect, self.app.VERSION, self.app.log)
        self.assertTrue(restored.status()["blocked"])
        state = restored.check(release_fetch("2.0.0", "2.1.0"))
        self.assertEqual(state["minimumSafeServerVersion"], "2.2.0")
        self.assertTrue(state["blocked"])

    def test_newer_release_need_not_block(self):
        state = self.updates.check(release_fetch("2.0.0", "2.2.0"))
        self.assertTrue(state["updateAvailable"])
        self.assertFalse(state["blocked"])

    def test_transaction_gate_preserves_completion_and_release(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            first = self.app.reserve_next()
            self.app.transition(first["reservationId"], "printing")
            second = self.app.reserve_next()
            self.updates.check(release_fetch())
            with self.assertRaises(self.app.AdmissionBlocked):
                self.app.reserve_next()
            with self.assertRaises(self.app.AdmissionBlocked):
                self.app.transition(second["reservationId"], "printing")
            with self.assertRaises(self.app.AdmissionBlocked):
                self.app.secondary_label({"printId": "a" * 32, "asnNumber": 1, "documentId": 1})
            self.assertEqual(self.app.transition(first["reservationId"], "printed")["status"], "printed")
            self.app.transition(second["reservationId"], "released")

    def test_api_block_has_actionable_error_and_no_allocation(self):
        self.updates.check(release_fetch())
        statuses = []
        body = self.app.wsgi_app({"REQUEST_METHOD": "POST", "PATH_INFO": "/api/reservations/next",
            "wsgi.input": io.BytesIO(), "HTTP_AUTHORIZATION": "Bearer secret",
            "HTTP_X_ASN_CLIENT_VERSION": "2.1.1", "HTTP_X_ASN_API_VERSION": "1"},
            lambda status, headers: statuses.append(status))
        self.assertTrue(statuses[0].startswith("503"))
        self.assertEqual(json.loads(body[0])["errorCode"], "server_update_required")

    def test_untrusted_urls_and_bad_policies_rejected(self):
        for url in ("http://github.com/NightmindOfficial/asn-labeler/releases/download/x",
                    "https://evil.test/x", "https://user@github.com/NightmindOfficial/asn-labeler/releases/download/x"):
            self.assertFalse(allowed_url(url))
        with self.assertRaises(ValueError):
            latest_policy(release_fetch("3.0.0", "2.2.0"))
