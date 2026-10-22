import importlib.util
import io
import http.client
import json
import os
import sqlite3
import tempfile
import threading
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from unittest import mock
from urllib import request
from urllib.error import HTTPError

from test_app import load_app


class ProductionServerTests(unittest.TestCase):
    def test_wsgi_rejects_old_ui_version_before_mutating(self):
        with tempfile.TemporaryDirectory() as folder:
            app = load_app(os.path.join(folder, "db"))
            response = []
            env = {"REQUEST_METHOD": "POST", "PATH_INFO": "/api/reservations/next",
                   "HTTP_AUTHORIZATION": "Bearer secret", "HTTP_X_ASN_VERSION": "old-version",
                   "CONTENT_LENGTH": "2", "wsgi.input": io.BytesIO(b"{}")}
            with mock.patch.object(app, "reserve_next") as reserve:
                result = app.wsgi_app(env, lambda status, headers: response.append((status, headers)))
            self.assertEqual(response[0][0], "409 Conflict")
            self.assertEqual(json.loads(result[0])["errorCode"], "admin_reload_required")
            reserve.assert_not_called()

    def test_waitress_http_concurrency_body_limits_and_auth(self):
        from waitress import create_server, wasyncore
        with tempfile.TemporaryDirectory() as folder:
            app = load_app(os.path.join(folder, "db"))
            app.db_connect().close()  # Same migration-before-listening sequence as main().
            channels = {}
            server = create_server(app.wsgi_app, host="127.0.0.1", port=0, map=channels,
                threads=4, max_request_body_size=4096, max_request_header_size=16384,
                asyncore_loop_timeout=0.1, clear_untrusted_proxy_headers=True)
            stop = threading.Event()
            def serve_test():
                while not stop.is_set():
                    wasyncore.loop(timeout=0.1, map=channels, count=1)
            thread = threading.Thread(target=serve_test, daemon=True)
            thread.start()
            base = "http://127.0.0.1:" + str(server.effective_port)
            try:
                def reserve(_):
                    req = request.Request(base + "/api/reservations/next", method="POST",
                                          headers={"Authorization": "Bearer secret"})
                    with request.urlopen(req, timeout=5) as response:
                        return json.load(response)["asnNumber"]
                with mock.patch.object(app, "paperless_max_asn", return_value=0), ThreadPoolExecutor(4) as pool:
                    self.assertEqual(sorted(pool.map(reserve, range(12))), list(range(1, 13)))
                # Send only headers: an oversized declared body must be rejected
                # before upload, without Windows resetting an unread request body.
                conn = http.client.HTTPConnection("127.0.0.1", server.effective_port, timeout=3)
                try:
                    conn.putrequest("POST", "/api/reservations/next")
                    conn.putheader("Content-Length", "5000")
                    conn.putheader("Authorization", "Bearer secret")
                    conn.endheaders()
                    self.assertEqual(conn.getresponse().status, 413)
                finally:
                    conn.close()
                with self.assertRaises(HTTPError) as exc:
                    request.urlopen(base + "/api/admin/state", timeout=3)
                self.assertEqual(exc.exception.code, 401)
                self.assertEqual(app.admin_state()["total"], 12)
            finally:
                # Stop the event loop before closing its socket map from this thread.
                stop.set()
                server.pull_trigger()
                thread.join(3)
                server.task_dispatcher.shutdown()
                server.close()
                wasyncore.close_all(channels)
                self.assertFalse(thread.is_alive())

    def test_wsgi_methods_and_health(self):
        with tempfile.TemporaryDirectory() as folder:
            app = load_app(os.path.join(folder, "db"))
            response = []
            env = {"REQUEST_METHOD": "GET", "PATH_INFO": "/health", "wsgi.input": io.BytesIO()}
            result = app.wsgi_app(env, lambda status, headers: response.append((status, headers)))
            self.assertEqual(response[0][0], "200 OK")
            self.assertIn("safe-sync-v1", json.loads(result[0])["capabilities"])
            env["REQUEST_METHOD"] = "DELETE"
            result = app.wsgi_app(env, lambda status, headers: response.append((status, headers)))
            self.assertEqual(response[-1][0], "405 Method Not Allowed")

    def test_backup_includes_wal_and_is_readable(self):
        path = os.path.join(os.path.dirname(__file__), "..", "admin.py")
        spec = importlib.util.spec_from_file_location("admin_console", path)
        admin = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(admin)
        with tempfile.TemporaryDirectory() as folder:
            db = os.path.join(folder, "db.sqlite3")
            app = load_app(db)
            with mock.patch.object(app, "paperless_max_asn", return_value=0):
                row = app.reserve_next()
            with mock.patch.dict(os.environ, {"SQLITE_PATH": db}):
                result = admin.backup()
            with closing(sqlite3.connect(result["backup"])) as conn:
                self.assertEqual(conn.execute("SELECT id FROM reservations").fetchone()[0], row["reservationId"])
                self.assertEqual(conn.execute("SELECT COUNT(*) FROM audit_events").fetchone()[0], 1)


if __name__ == "__main__":
    unittest.main()
