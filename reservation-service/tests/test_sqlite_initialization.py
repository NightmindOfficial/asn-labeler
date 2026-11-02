import os
import sqlite3
import tempfile
import threading
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
from unittest import mock

from test_app import load_app


class SqliteInitializationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = os.path.join(self.temp.name, "db.sqlite3")
        self.app = load_app(self.path)

    def test_wal_activation_retries_real_lock_then_restores_timeout(self):
        retrying = threading.Event()
        real_sleep = self.app.time.sleep

        def retry_sleep(delay):
            retrying.set()
            real_sleep(delay)

        def connect():
            with closing(self.app.db_connect()) as conn:
                return (conn.execute("PRAGMA journal_mode").fetchone()[0],
                        conn.execute("PRAGMA busy_timeout").fetchone()[0],
                        conn.execute("PRAGMA synchronous").fetchone()[0])

        with closing(sqlite3.connect(self.path, isolation_level=None)) as blocker:
            blocker.execute("BEGIN EXCLUSIVE")
            with mock.patch.object(self.app.time, "sleep", side_effect=retry_sleep), ThreadPoolExecutor(max_workers=1) as pool:
                future = pool.submit(connect)
                try:
                    self.assertTrue(retrying.wait(3), "WAL initialization must retry the lock")
                finally:
                    blocker.rollback()
                self.assertEqual(future.result(timeout=5), ("wal", 10000, 2))

    def test_busy_retry_has_deadline(self):
        conn = mock.Mock()
        busy = sqlite3.OperationalError("database is locked")
        busy.sqlite_errorcode = sqlite3.SQLITE_BUSY
        conn.execute.side_effect = [None, busy, None]
        with mock.patch.object(self.app.time, "monotonic", side_effect=[0, 11]), mock.patch.object(self.app.time, "sleep") as sleep:
            with self.assertRaises(sqlite3.OperationalError) as raised:
                self.app.enable_wal(conn)
        self.assertIs(raised.exception, busy)
        sleep.assert_not_called()
        self.assertEqual(conn.execute.call_args.args, ("PRAGMA busy_timeout=10000",))

    def test_non_lock_error_is_not_retried(self):
        conn = mock.Mock()
        failure = sqlite3.OperationalError("disk I/O error")
        failure.sqlite_errorcode = sqlite3.SQLITE_IOERR
        conn.execute.side_effect = [None, failure, None]
        with mock.patch.object(self.app.time, "sleep") as sleep:
            with self.assertRaises(sqlite3.OperationalError) as raised:
                self.app.enable_wal(conn)
        self.assertIs(raised.exception, failure)
        sleep.assert_not_called()

    def test_existing_wal_connection_works_during_writer_transaction(self):
        with closing(self.app.db_connect()) as writer:
            writer.execute("BEGIN IMMEDIATE")
            try:
                with closing(self.app.db_connect()) as reader:
                    self.assertEqual(reader.execute("PRAGMA journal_mode").fetchone()[0], "wal")
            finally:
                writer.rollback()

    def test_concurrent_first_connections_with_independent_service_instances(self):
        for attempt in range(20):
            path = os.path.join(self.temp.name, str(attempt) + ".sqlite3")
            apps = [load_app(path) for _ in range(4)]
            barrier = threading.Barrier(4)

            def connect(app):
                barrier.wait(timeout=5)
                with closing(app.db_connect()) as conn:
                    return conn.execute("PRAGMA user_version").fetchone()[0]

            with ThreadPoolExecutor(max_workers=4) as pool:
                self.assertEqual(list(pool.map(connect, apps)), [3] * 4)
            with closing(sqlite3.connect(path)) as conn:
                self.assertEqual(conn.execute("PRAGMA integrity_check").fetchone()[0], "ok")
