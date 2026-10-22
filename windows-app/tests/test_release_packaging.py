"""Regression checks for the release's public file boundary."""
from pathlib import Path
import runpy
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
PACKAGING = runpy.run_path(str(ROOT / "package-release.py"))


class ReleasePackagingTests(unittest.TestCase):
    def test_only_named_public_documents_are_selected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in (".local/docs/BACKLOG.md", "docs/private.md", "AUDIT.md",
                         "windows-app/src/Example.cs", "windows-app/tools/file.download"):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("test fixture", encoding="utf-8")
            names = {p.relative_to(root).as_posix() for p in PACKAGING["client_source_files"](root)}
            self.assertIn("windows-app/src/Example.cs", names)
            self.assertIn("LICENSE", names)
            self.assertIn("LICENSE-BPAC-EXCEPTION.txt", names)
            self.assertIn("THIRD-PARTY-NOTICES.md", names)
            self.assertEqual({n for n in names if n.endswith(".md")}, set(PACKAGING["PUBLIC_DOCUMENTS"]))
            self.assertFalse(any(n.startswith(".local/") or n.endswith(".download") for n in names))

    def test_all_selected_files_exist(self):
        for path in PACKAGING["client_source_files"](ROOT):
            self.assertTrue(path.is_file(), str(path))


if __name__ == "__main__":
    unittest.main()
