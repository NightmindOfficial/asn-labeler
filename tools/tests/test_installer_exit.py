"""Exercise the installer test's final checks without installing software."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
PWSH = shutil.which("pwsh")


@unittest.skipUnless(PWSH, "PowerShell 7 is required")
class InstallerExitTests(unittest.TestCase):
    def run_final_checks(self, registry_exists=False, path_changed=False):
        source = (ROOT / "windows-app/tests/test-installer.ps1").read_text(encoding="utf-8")
        footer = source[source.index("# Final checks must pass"):]
        with tempfile.TemporaryDirectory() as directory:
            script = Path(directory) / "final-checks.ps1"
            script.write_text(
                "$ErrorActionPreference = 'Stop'\n"
                "$global:LASTEXITCODE = 2\n"
                "$registry = 'unused'\n"
                "$pathBefore = 'original'\n"
                "function Test-Path { return $" + str(registry_exists).lower() + " }\n"
                "function Read-Path { return '" + ("changed" if path_changed else "original") + "' }\n"
                + footer,
                encoding="utf-8",
            )
            # Match Actions' wrapper: a native exit code survives script calls
            # unless the test explicitly reports success after its assertions.
            return subprocess.run(
                [PWSH, "-NoProfile", "-NonInteractive", "-Command",
                 "$ErrorActionPreference = 'Stop'; & '" + str(script).replace("'", "''")
                 + "'; if (Test-Path variable:LASTEXITCODE) { exit $LASTEXITCODE }"],
                capture_output=True, text=True, timeout=30,
            )

    def test_expected_cli_error_does_not_fail_successful_test(self):
        result = self.run_final_checks()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("uninstall passed", result.stdout)

    def test_registry_cleanup_failure_still_fails(self):
        result = self.run_final_checks(registry_exists=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Uninstall registration was not removed", result.stderr)
        self.assertNotIn("uninstall passed", result.stdout)

    def test_path_cleanup_failure_still_fails(self):
        result = self.run_final_checks(path_changed=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Uninstall altered unrelated PATH entries", result.stderr)
        self.assertNotIn("uninstall passed", result.stdout)
