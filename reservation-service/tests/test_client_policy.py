import io
import json
import os
import tempfile
import unittest
from unittest import mock

from test_app import load_app


class ClientPolicyTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.addCleanup(self.folder.cleanup)
        self.app = load_app(os.path.join(self.folder.name, "db"))
        self.app.CONFIG.update(admin_keys={"admin"})

    def call(self, path, version=None, key="secret", method="GET", api="1"):
        env = {"REQUEST_METHOD": method, "PATH_INFO": path, "wsgi.input": io.BytesIO(),
               "HTTP_AUTHORIZATION": "Bearer " + key, "HTTP_X_ASN_API_VERSION": api}
        if version is not None:
            env["HTTP_X_ASN_CLIENT_VERSION"] = version
        statuses = []
        body = self.app.wsgi_app(env, lambda status, headers: statuses.append(int(status.split()[0])))
        return statuses[0], json.loads(body[0])

    def test_health_advertises_embedded_minimum_ignoring_legacy_config(self):
        self.assertEqual(self.call("/health", key="")[1]["minimumClientVersion"], "2.1.1")
        self.app.CONFIG["minimum_client_version"] = "2.1.3"
        self.assertEqual(self.call("/health", key="")[1]["minimumClientVersion"], "2.1.1")
        self.app.CONFIG["minimum_client_version"] = "1.0.0"
        self.assertEqual(self.call("/health")[1]["minimumClientVersion"], "2.1.1")

    def test_missing_old_malformed_clients_cannot_start_work(self):
        with mock.patch.object(self.app, "reserve_next") as reserve:
            for version in (None, "", "1.4.3", "2.0.0", "2.1.0", "2.0", "02.0.0", "2.0.0-beta", "999999.0.0", "2.0.0\n"):
                with self.subTest(version=version):
                    status, body = self.call("/api/reservations/next", version, method="POST")
                    self.assertEqual(status, 426)
                    self.assertEqual(body["errorCode"], "client_update_required")
                    self.assertEqual(body["minimumClientVersion"], "2.1.1")
            reserve.assert_not_called()

    def test_valid_future_clients_and_admin_remain_usable(self):
        with mock.patch.object(self.app, "peek_next", return_value={"asnNumber": 1}):
            for version in ("2.1.1", "2.1.2", "3.0.0"):
                self.assertEqual(self.call("/api/reservations/next", version)[0], 200)
            self.assertEqual(self.call("/api/reservations/next", key="admin")[0], 200)
            self.assertEqual(self.call("/api/reservations/next", "1.4.3", "admin")[0], 426)
            self.assertEqual(self.call("/api/reservations/next", "2.1.1", api="2")[0], 409)
            self.assertEqual(self.call("/api/reservations/next", "2.1.1", key="bad")[0], 401)

    def test_raise_minimum_preserves_owned_print_completion_but_not_new_work(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            status, row = self.call("/api/reservations/next", "2.1.1", method="POST")
        self.assertEqual(status, 200)
        path = "/api/reservations/" + row["reservationId"]
        self.assertEqual(self.call(path + "/printing", "2.1.1", method="POST")[0], 200)
        self.app.CLIENT_VERSION_FLOOR = "2.1.2"
        self.app.CONFIG["api_keys"].add("other")
        self.assertEqual(self.call(path + "/printed", "2.1.1", "other", "POST")[0], 403)
        self.assertEqual(self.call(path + "/printed", "2.1.1", method="POST")[0], 200)
        self.assertEqual(self.call("/api/reservations/next", "2.1.1", method="POST")[0], 426)
        self.assertEqual(self.call(path + "/printing", "2.1.1", method="POST")[0], 426)

    def test_unsupported_client_can_only_release_never_started_own_reservation(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            _, row = self.call("/api/reservations/next", "2.1.1", method="POST")
            self.app.CLIENT_VERSION_FLOOR = "2.1.2"
            self.assertEqual(self.call("/api/reservations/" + row["reservationId"] + "/release", "2.1.1", method="POST")[0], 200)

    def test_secondary_confirmation_exception_is_exact(self):
        with mock.patch.object(self.app, "secondary_label", return_value={"ok": True}) as secondary:
            self.assertEqual(self.call("/api/documents/copy-labels/start", "1.4.3", method="POST")[0], 426)
            secondary.assert_not_called()
            self.assertEqual(self.call("/api/documents/copy-labels/printed", "1.4.3", method="POST")[0], 200)
            secondary.assert_called_once()

    def test_invalid_policy_is_rejected(self):
        for value in ("", "2.0", "65535.0.0", "1.2.3\n"):
            self.app.CLIENT_VERSION_FLOOR = value
            with self.assertRaises(ValueError):
                self.app.validate_config()
