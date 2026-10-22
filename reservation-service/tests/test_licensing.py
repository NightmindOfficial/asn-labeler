import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from zipfile import ZipFile
from test_app import load_app


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("package_source", ROOT / "package_source.py")
packaging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packaging)


class LicensingTests(unittest.TestCase):
    def test_internal_documents_are_not_distributed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in packaging.FILES:
                (root / name).write_text("public source", encoding="utf-8")
            for name in packaging.DIRECTORIES:
                (root / name).mkdir()
            (root / ".local").mkdir()
            (root / ".local/notes.md").write_text("private notes", encoding="utf-8")
            (root / "AUDIT.md").write_text("old internal audit", encoding="utf-8")
            (root / ".env").write_text("secret", encoding="utf-8")
            output = root / "source.zip"
            packaging.build_source(root, output)
            with ZipFile(output) as archive:
                self.assertEqual(set(archive.namelist()), set(packaging.FILES))
                self.assertIn("README.md", archive.namelist())

    def test_source_archive_contains_build_inputs_not_user_data(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "source.zip"
            packaging.build_source(ROOT, output)
            with ZipFile(output) as archive:
                names = archive.namelist()
                for name in ("app.py", "Dockerfile", "package_source.py", "requirements.txt",
                             "licenses/LICENSE", "licenses/Waitress-LICENSE.txt", "web/index.html",
                             "tests/test_licensing.py", ".env.example"):
                    self.assertIn(name, names)
                self.assertFalse(any(n == ".env" or ".sqlite" in n or n.endswith(".zip") for n in names))
                self.assertEqual(archive.read("app.py"), (ROOT / "app.py").read_bytes())

    def test_license_and_source_available_without_login(self):
        with tempfile.TemporaryDirectory() as directory:
            app = load_app(str(Path(directory) / "db.sqlite3"))
            for path in ("/admin/license", "/admin/notices", "/admin/source.zip"):
                request = app.ApiRequest("GET", path, {}, io.BytesIO(), "local")
                request.dispatch("GET")
                status, headers, body = request.response
                self.assertEqual(status, 200)
                if path.endswith(".zip"):
                    with ZipFile(io.BytesIO(body)) as archive:
                        self.assertIn("app.py", archive.namelist())
                    self.assertIn("attachment", dict(headers)["Content-Disposition"])
                elif path.endswith("license"):
                    self.assertIn(b"GNU AFFERO GENERAL PUBLIC LICENSE", body)
