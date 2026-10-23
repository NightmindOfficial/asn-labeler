import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_release import needs_version_bump, validate_progression
import check_release
from release_common import asset_names, read_version, release_notes, version_tuple, verify_manifest, write_manifest
from publish_release import GitHub, find_release, publish, main as publish_main
from verify_artifacts import validate_archive


class VersionTests(unittest.TestCase):
    def test_independent_version_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for folder, version in (("windows-app", "2.0.0"), ("reservation-service", "1.4.4")):
                (root / folder).mkdir()
                (root / folder / "VERSION").write_text(version, encoding="utf-8")
            self.assertEqual(read_version(root, "client"), "2.0.0")
            self.assertEqual(read_version(root, "server"), "1.4.4")

    def test_runtime_changes_require_only_affected_version(self):
        self.assertTrue(needs_version_bump("client", ["windows-app/src/Program.cs"]))
        self.assertFalse(needs_version_bump("server", ["windows-app/src/Program.cs"]))
        self.assertTrue(needs_version_bump("server", ["reservation-service/app.py"]))
        self.assertFalse(needs_version_bump("client", ["reservation-service/app.py"]))
        for component in ("client", "server"):
            self.assertFalse(needs_version_bump(component, ["README.md", "docs/USAGE.md", ".github/workflows/ci.yml", "tools/check_release.py"]))
            self.assertTrue(needs_version_bump(component, ["LICENSE"]))

    def test_versions_and_assembly_limits(self):
        self.assertEqual(version_tuple("1.10.2"), (1, 10, 2))
        for value in ("1.2", "v1.2.3", "01.2.3", "1.2.3-rc1", "65535.1.0", "1.2.3\n"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                version_tuple(value)

    def test_changelog_exact_unique_nonempty_heading(self):
        text = "# Changes\n\n## 1.2.3\n\n- Fixed issue\n\n## 1.2.0\nOld"
        self.assertEqual(release_notes(text, "1.2.3"), "- Fixed issue")
        for text in ("## 1.2.0\nOld", "## 1.2.3\n\n", "## 1.2.3\nOne\n## 1.2.3\nTwo"):
            with self.assertRaises(ValueError):
                release_notes(text, "1.2.3")

    def test_rollbacks_and_reused_version_are_rejected(self):
        validate_progression("1.4.3", None, [])  # First repository import.
        validate_progression("1.4.3", "1.4.3", ["v1.4.3"])  # No-release change.
        validate_progression("1.4.4", "1.4.3", ["v1.4.3"])
        validate_progression("1.4.4", "1.4.3", ["v1.4.4"], ["v1.4.4"])  # Retry same commit.
        for previous, tags in (("1.4.4", []), (None, ["v1.5.0"]), ("1.4.2", ["v1.4.3"])):
            with self.assertRaises(ValueError):
                validate_progression("1.4.3", previous, tags)


class ReleaseSelectionTests(unittest.TestCase):
    def select(self, client, server, paths):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for folder, version in (("windows-app", client), ("reservation-service", server)):
                (root / folder).mkdir()
                (root / folder / "VERSION").write_text(version, encoding="utf-8")
                (root / folder / "CHANGELOG.md").write_text("## " + version + "\nNotes\n", encoding="utf-8")

            def git_output(args, **kwargs):
                if args[1] == "diff":
                    return "\n".join(paths)
                if args[1] == "tag":
                    return "client-v1.4.3\nserver-v1.4.3\n"
                if args[1] == "rev-parse":
                    return "b" * 40 if args[2] == "HEAD" else "a" * 40
                raise AssertionError(args)

            output = root / "outputs"
            with patch.object(check_release, "ROOT", root), \
                    patch.object(check_release.subprocess, "check_output", side_effect=git_output), \
                    patch.object(check_release.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout="1.4.3")), \
                    patch.dict("os.environ", {"CI_BASE_SHA": "a" * 40, "GITHUB_OUTPUT": str(output)}):
                check_release.main()
            return dict(line.split("=", 1) for line in output.read_text().splitlines())

    def test_only_server_releases(self):
        result = self.select("1.4.3", "1.4.4", ["reservation-service/app.py"])
        self.assertEqual(result["client_release"], "false")
        self.assertEqual(result["server_release"], "true")

    def test_only_client_releases(self):
        result = self.select("2.0.0", "1.4.3", ["windows-app/src/Program.cs"])
        self.assertEqual(result["client_release"], "true")
        self.assertEqual(result["server_release"], "false")

    def test_both_release_independently(self):
        result = self.select("2.0.0", "1.5.0", ["windows-app/src/Program.cs", "reservation-service/app.py"])
        self.assertEqual(result["client_release"], "true")
        self.assertEqual(result["server_release"], "true")
        self.assertEqual(result["client_version"], "2.0.0")
        self.assertEqual(result["server_version"], "1.5.0")

    def test_docs_do_not_release(self):
        result = self.select("1.4.3", "1.4.3", ["README.md"])
        self.assertEqual(result["client_release"], "false")
        self.assertEqual(result["server_release"], "false")

    def test_missing_component_bump_fails(self):
        with self.assertRaises(ValueError):
            self.select("1.4.4", "1.4.3", ["reservation-service/app.py"])


class ArtifactTests(unittest.TestCase):
    def test_client_2_uses_setup_not_portable(self):
        self.assertEqual(asset_names("2.0.0", "client"), ["asn-labeler-2.0.0-setup.exe", "asn-labeler-2.0.0-source.zip"])
        self.assertEqual(asset_names("1.4.3", "client")[0], "asn-labeler.exe")
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in asset_names("1.2.3"):
            (self.root / name).write_bytes(b"payload")
        write_manifest(self.root, "1.2.3", "a" * 40)

    def test_manifest_matches_version_commit_and_files(self):
        self.assertEqual(len(verify_manifest(self.root, "1.2.3", "a" * 40)), 4)
        for version, commit in (("1.2.4", "a" * 40), ("1.2.3", "b" * 40)):
            with self.assertRaises(ValueError):
                verify_manifest(self.root, version, commit)

    def test_modified_asset_is_rejected(self):
        (self.root / "asn-labeler.exe").write_bytes(b"different")
        with self.assertRaises(ValueError):
            verify_manifest(self.root, "1.2.3", "a" * 40)

    def test_component_mismatch_is_rejected(self):
        with self.assertRaises(ValueError):
            verify_manifest(self.root, "1.2.3", "a" * 40, "server")

    def test_server_manifest_does_not_need_client_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "asn-reservation-2.0.0-nas.zip").write_bytes(b"test")
            write_manifest(root, "2.0.0", "a" * 40, "server")
            self.assertEqual(len(verify_manifest(root, "2.0.0", "a" * 40, "server")), 3)

    def test_extra_file_is_rejected(self):
        (self.root / ".env").write_text("private", encoding="utf-8")
        with self.assertRaises(ValueError):
            verify_manifest(self.root, "1.2.3", "a" * 40)

    def test_modified_checksum_list_is_rejected(self):
        (self.root / "SHA256SUMS.txt").write_text("bad", encoding="utf-8")
        with self.assertRaises(ValueError):
            verify_manifest(self.root, "1.2.3", "a" * 40)

    def test_private_and_traversal_archive_entries_rejected(self):
        for name in (".env", ".env.production", ".local/notes.md", "db.sqlite3-wal", "../escape", "cert.pfx"):
            memory = io.BytesIO()
            with ZipFile(memory, "w") as archive:
                archive.writestr(name, "bad")
            with ZipFile(io.BytesIO(memory.getvalue())) as archive, self.assertRaises(ValueError):
                validate_archive(archive, [])


class FakeGitHub:
    def __init__(self, existing=None, tagged=None, fail_upload=False, advance=False):
        self.existing = existing
        self.tagged = tagged
        self.fail_upload = fail_upload
        self.advance = advance
        self.main_reads = 0
        self.writes = []
        self.assets = []

    def tag_commit(self, tag):
        return self.tagged

    def request(self, method, path, data=None, binary=None):
        if method != "GET":
            self.writes.append((method, path))
        if path in ("/releases/tags/client-v1.2.3", "/releases/tags/server-v1.2.3"):
            return self.existing if self.existing and not self.existing["draft"] else None
        if path == "/releases?per_page=100&page=1":
            return [dict(self.existing, tag_name="client-v1.2.3")] if self.existing else []
        if path == "/git/ref/heads/main":
            self.main_reads += 1
            return {"object": {"sha": "b" * 40 if self.advance and self.main_reads > 1 else "a" * 40}}
        if method == "POST" and path == "/releases":
            assert data["draft"] is True
            self.created = data
            return {"id": 42, "upload_url": "https://uploads.github.com/release{?name}"}
        if path == "/releases/42/assets?per_page=100":
            return self.assets.copy()
        if method == "POST" and path.startswith("https://uploads.github.com/"):
            if self.fail_upload:
                raise RuntimeError("Simulated upload failure")
            self.assets.append({"name": path.split("?name=")[1], "size": len(binary), "state": "uploaded", "id": len(self.assets) + 1})
            return {}
        if method == "DELETE":
            self.assets = [a for a in self.assets if a["id"] != int(path.rsplit("/", 1)[1])]
            return None
        if method == "PATCH":
            assert data["draft"] is False
            assert data["make_latest"] == "false"
            return {}
        raise AssertionError((method, path))


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "test.exe").write_bytes(b"test")

    def run_publish(self, api):
        return publish(api, self.root, "1.2.3", "a" * 40, "Notes", ["test.exe"])

    def test_published_release_never_changes(self):
        api = FakeGitHub(existing={"draft": False})
        self.assertEqual(self.run_publish(api), "unchanged")
        self.assertEqual(api.writes, [])

    def test_successful_upload_is_published_last(self):
        api = FakeGitHub()
        self.assertEqual(self.run_publish(api), "published")
        self.assertEqual(api.writes[-1], ("PATCH", "/releases/42"))
        self.assertEqual(api.created["tag_name"], "client-v1.2.3")

    def test_server_release_has_independent_tag(self):
        api = FakeGitHub()
        publish(api, self.root, "1.2.3", "a" * 40, "Notes", ["test.exe"], "server")
        self.assertEqual(api.created["tag_name"], "server-v1.2.3")

    def test_failed_upload_stays_draft(self):
        api = FakeGitHub(fail_upload=True)
        with self.assertRaises(RuntimeError):
            self.run_publish(api)
        self.assertFalse(any(method == "PATCH" for method, _ in api.writes))

    def test_draft_retry_replaces_only_draft_assets(self):
        api = FakeGitHub(existing={"draft": True, "id": 42, "target_commitish": "a" * 40,
                                  "upload_url": "https://uploads.github.com/release{?name}"})
        api.assets = [{"id": 1, "name": "test.exe"}]
        self.assertEqual(self.run_publish(api), "published")
        self.assertIn(("DELETE", "/releases/assets/1"), api.writes)
        self.assertNotIn(("POST", "/releases"), api.writes)

    def test_draft_discovery_paginates(self):
        api = FakeGitHub()
        draft = {"tag_name": "client-v1.2.3", "draft": True}
        with patch.object(api, "request", side_effect=[None, [{"tag_name": "v0.1.0"}] * 100, [draft]]) as request:
            self.assertEqual(find_release(api, "client-v1.2.3"), draft)
            self.assertEqual(request.call_args.args, ("GET", "/releases?per_page=100&page=2"))

    def test_draft_discovery_limit_fails_without_writes(self):
        api = FakeGitHub()
        with patch.object(api, "request", side_effect=[None] + [[{"tag_name": "v0.1.0"}] * 100] * 100), self.assertRaises(ValueError):
            find_release(api, "client-v1.2.3")
        self.assertEqual(api.writes, [])

    def test_conflicting_tag_or_draft_is_rejected(self):
        for api in (FakeGitHub(tagged="b" * 40), FakeGitHub(existing={"draft": True, "target_commitish": "b" * 40})):
            with self.assertRaises(ValueError):
                self.run_publish(api)
            self.assertEqual(api.writes, [])

    def test_main_advances_during_upload(self):
        api = FakeGitHub(advance=True)
        with self.assertRaises(ValueError):
            self.run_publish(api)
        self.assertFalse(any(method == "PATCH" for method, _ in api.writes))

    def test_pr_and_other_branches_cannot_publish(self):
        for event, ref in (("pull_request", "refs/heads/main"), ("push", "refs/heads/feature")):
            with patch.dict("os.environ", {"GITHUB_EVENT_NAME": event, "GITHUB_REF": ref}), self.assertRaises(ValueError):
                publish_main()

    def test_credentials_are_not_sent_to_arbitrary_upload_host(self):
        with self.assertRaises(ValueError):
            GitHub("owner/repo", "fake").request("POST", "https://example.com/upload", binary=b"data")


if __name__ == "__main__":
    unittest.main()
