import io
import json
import os
import sqlite3
import tempfile
import unittest
from unittest import mock
from urllib.error import HTTPError
from test_app import load_app


class WebAdminTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.app = load_app(os.path.join(self.temp.name, "db.sqlite3"))
        self.app.CONFIG["admin_keys"] = {"admin-key"}
        self.app.db_connect().close()

    def call(self, path, key="admin-key", body=None, instance=None, version=None):
        data = json.dumps(body).encode() if body is not None else b""
        headers = {"Authorization": "Bearer " + key, "Content-Length": str(len(data))}
        if instance is not None:
            headers["X-ASN-Instance"] = instance
        if version is not None:
            headers["X-ASN-Version"] = version
        req = self.app.ApiRequest("POST" if body is not None else "GET", path,
            headers, io.BytesIO(data), "local")
        req.dispatch(req.command)
        return req.response

    def test_shell_and_health_identify_service_instance(self):
        for path in ("/admin/", "/health"):
            status, headers, data = self.call(path)
            self.assertEqual(status, 200)
            self.assertEqual(dict(headers)["X-ASN-Instance"], self.app.INSTANCE_ID)
            self.assertEqual(dict(headers)["X-ASN-Version"], self.app.VERSION)
            if path == "/admin/":
                self.assertIn(self.app.VERSION.encode(), data)
                self.assertNotIn(b"__ASN_VERSION__", data)
                self.assertIn(('id="version">Reservation Service ' + self.app.VERSION + '</span>').encode(), data)
                self.assertIn(('id="footerVersion">' + self.app.VERSION + '</span>').encode(), data)

    def test_stale_version_cannot_mutate_after_update(self):
        with mock.patch.object(self.app, "reset_to_paperless") as reset:
            status, _, data = self.call("/api/admin/reset-to-paperless", body={}, version="1.4.0")
        self.assertEqual(status, 409)
        self.assertEqual(json.loads(data)["errorCode"], "admin_reload_required")
        reset.assert_not_called()

    def test_public_health_exposes_only_liveness_and_client_contract(self):
        with mock.patch.object(self.app.server_updates(), "status") as diagnostics:
            status, _, body = self.call("/health", key="")
        self.assertEqual(status, 200)
        data = json.loads(body)
        self.assertEqual(set(data), {"ok", "version", "time", "apiVersion",
                                    "minimumClientVersion", "capabilities", "requestId"})
        self.assertTrue(data["ok"])
        self.assertEqual(data["apiVersion"], 1)
        diagnostics.assert_not_called()

    def test_public_documents_are_plain_text_and_keep_source_offer(self):
        for path in ("/admin/license", "/admin/notices", "/admin/changelog"):
            status, headers, body = self.call(path, key="")
            self.assertEqual(status, 200)
            self.assertEqual(dict(headers)["Content-Type"], "text/plain; charset=utf-8")
            self.assertTrue(body)
            if path.endswith("changelog"):
                self.assertIn(("## " + self.app.VERSION).encode(), body)
        shell = self.call("/admin/", key="")[2].decode()
        self.assertIn('id="documentSource" href="/admin/source.zip"', shell)
        self.assertNotIn('/admin/source.zip', shell.split('<footer>')[1])

    def test_current_and_legacy_clients_remain_compatible(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            for version in (None, self.app.VERSION):
                self.assertEqual(self.call("/api/reservations/next", body={}, version=version)[0], 200)

    def test_same_version_restart_keeps_admin_session_usable(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            status, _, _ = self.call("/api/reservations/next", body={}, instance="previous-boot", version=self.app.VERSION)
        self.assertEqual(status, 200)

    def test_shell_no_secrets_and_strict_security_headers(self):
        for path in ("/admin", "/admin/", "/admin/app.js", "/admin/i18n.js", "/admin/style.css", "/admin/icon.ico"):
            status, headers, data = self.call(path, key="")
            self.assertEqual(status, 200)
            self.assertIn("frame-ancestors 'none'", dict(headers)["Content-Security-Policy"])
            self.assertNotIn(b"admin-key", data)
        self.assertEqual(self.call("/admin/../app.py", key="")[0], 401)

    def test_admin_endpoints_require_admin_not_client(self):
        for path, body in (("/api/admin/diagnostics", None), ("/api/admin/logs", None), ("/api/admin/backup", {})):
            self.assertEqual(self.call(path, "secret", body)[0], 401)
            self.assertEqual(self.call(path, "", body)[0], 401)

    def test_diagnostics_is_read_only(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=57):
            status, _, body = self.call("/api/admin/diagnostics")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)["paperlessMax"], 57)
        self.assertEqual(self.app.admin_state()["total"], 0)
        self.assertEqual(self.app.admin_state()["paperlessHighWater"], 0)

    def test_400_details_sanitized_and_no_allocation(self):
        error = HTTPError("http://private", 400, "Bad request", {},
            io.BytesIO(b'{"archive_serial_number__gt":["Enter a number. SECRET_TOKEN"],"private-document-title":"hidden"}'))
        with mock.patch.object(self.app.request.OpenerDirector, "open", side_effect=error):
            status, _, body = self.call("/api/reservations/next", body={})
        self.assertEqual(status, 503)
        data = json.loads(body)
        self.assertEqual(data["errorCode"], "paperless_http_400")
        self.assertEqual(data["validationFields"], ["archive_serial_number__gt"])
        rendered = str(self.app.log.call_args_list) + body.decode()
        for secret in ("SECRET_TOKEN", "private-document-title", "http://private"):
            self.assertNotIn(secret, rendered)
        self.assertEqual(self.app.admin_state()["total"], 0)

    def test_html_400_still_has_actionable_hint(self):
        error = HTTPError("http://private", 400, "Bad request", {}, io.BytesIO(b'<html>secret-host</html>'))
        with mock.patch.object(self.app.request.OpenerDirector, "open", side_effect=error):
            status, _, body = self.call("/api/admin/diagnostics")
        self.assertEqual(status, 503)
        self.assertIn("Allowed Hosts", json.loads(body)["error"])
        self.assertNotIn(b"secret-host", body)

    def test_web_backup_is_consistent_with_audit(self):
        with mock.patch.object(self.app, "paperless_max_asn", return_value=0):
            self.app.reserve_next()
        status, headers, data = self.call("/api/admin/backup", body={})
        self.assertEqual(status, 200)
        self.assertIn("attachment", dict(headers)["Content-Disposition"])
        target = os.path.join(self.temp.name, "restored.sqlite3")
        with open(target, "wb") as out:
            out.write(data)
        conn = sqlite3.connect(target)
        try:
            self.assertEqual(conn.execute("PRAGMA integrity_check").fetchone()[0], "ok")
            self.assertEqual(conn.execute("SELECT COUNT(*) FROM reservations").fetchone()[0], 1)
            self.assertEqual(conn.execute("SELECT COUNT(*) FROM audit_events").fetchone()[0], 1)
        finally:
            conn.close()

    def test_logs_endpoint_returns_recent_events(self):
        self.app.RECENT_LOGS.append({"event": "service_started", "version": self.app.VERSION})
        status, _, body = self.call("/api/admin/logs")
        self.assertEqual(status, 200)
        self.assertEqual(json.loads(body)["items"][0]["version"], self.app.VERSION)

    def test_legacy_database_backed_up_before_migration(self):
        source = os.path.join(self.temp.name, "legacy.sqlite3")
        conn = sqlite3.connect(source)
        try:
            conn.execute("CREATE TABLE evidence (asn INTEGER)")
            conn.execute("INSERT INTO evidence VALUES (42)")
            conn.commit()
        finally:
            conn.close()
        self.app.CONFIG["sqlite_path"] = source
        self.app.backup_before_migration()
        from pathlib import Path
        backups = list((Path(self.temp.name) / "backups").glob("*.sqlite3"))
        self.assertEqual(len(backups), 1)
        conn = sqlite3.connect(backups[0])
        try:
            self.assertEqual(conn.execute("SELECT asn FROM evidence").fetchone()[0], 42)
            self.assertEqual(conn.execute("PRAGMA user_version").fetchone()[0], 0)
        finally:
            conn.close()


if __name__ == "__main__":
    unittest.main()
