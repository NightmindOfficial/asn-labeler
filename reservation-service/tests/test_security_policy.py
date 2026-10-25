import base64
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
from test_app import load_app
from security_policy import verify, fetch_policy
from server_updates import ServerUpdates


class SecurityPolicyTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.app = load_app(os.path.join(self.folder.name, "db"))
        # Policy scenarios intentionally exercise an older installed release.
        self.app.VERSION = "2.1.1"
        self.updates = self.app.server_updates()
        self.policy = dict(schema=1, revision=10, minimumClientVersion="2.2.0", minimumServerVersion="2.2.0", reason="test")

    def apply(self, policy):
        with mock.patch("server_updates.fetch_policy", return_value=policy):
            self.updates.check_security()

    def test_unknown_offline_is_allowed(self):
        self.updates.check_security(mock.Mock(side_effect=OSError()))
        self.assertFalse(self.updates.status()["blocked"])

    def test_floors_survive_restart_outage_and_rollback(self):
        self.apply(self.policy)
        self.assertEqual(self.app.minimum_client_version(), "2.2.0")
        self.updates.check_security(mock.Mock(side_effect=OSError()))
        self.assertTrue(ServerUpdates(self.updates.connect, self.app.VERSION, self.app.log).status()["blocked"])
        self.apply(dict(self.policy, revision=9, minimumClientVersion="2.0.0", minimumServerVersion="2.0.0"))
        self.apply(dict(self.policy, revision=11, minimumClientVersion="2.0.0"))
        self.assertEqual(self.app.minimum_client_version(), "2.2.0")
        self.assertTrue(self.updates.status()["blocked"])

    def test_existing_print_finishes_but_new_work_stops(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            first = self.app.reserve_next()
            self.app.transition(first["reservationId"], "printing")
            second = self.app.reserve_next()
            self.apply(self.policy)
            with self.assertRaises(self.app.AdmissionBlocked): self.app.reserve_next()
            with self.assertRaises(self.app.AdmissionBlocked): self.app.transition(second["reservationId"], "printing")
            self.app.transition(first["reservationId"], "printed")
            self.app.transition(second["reservationId"], "released")

    def test_missing_release_does_not_activate(self):
        with mock.patch("security_policy.verify", return_value=self.policy):
            with self.assertRaises(ValueError): fetch_policy(lambda url: {})

    def test_client_floor_is_checked_inside_allocation_transaction(self):
        self.apply(dict(self.policy, minimumServerVersion="2.0.0"))
        token = self.app.REQUEST_CLIENT_VERSION.set("2.1.1")
        try:
            with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
                with self.assertRaises(self.app.AdmissionBlocked) as error:
                    self.app.reserve_next()
                self.assertEqual(error.exception.code, "client_update_required")
            with self.updates.connect() as conn:
                self.assertEqual(conn.execute("SELECT COUNT(*) FROM reservations").fetchone()[0], 0)
        finally:
            self.app.REQUEST_CLIENT_VERSION.reset(token)

    def test_signature_rejects_tampering(self):
        envelope = {"payload": base64.b64encode(b'{}').decode(), "signature": base64.b64encode(bytes(384)).decode()}
        with self.assertRaises(ValueError): verify(envelope)
