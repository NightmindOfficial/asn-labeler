"""Fail CI on invalid versions, version rollback or missing release notes."""
import os
from pathlib import Path
import re
import subprocess
from release_common import read_version, release_notes, version_tuple

ROOT = Path(__file__).resolve().parents[1]


def validate_progression(version, previous=None, tags=()):
    current = version_tuple(version)
    if previous and current < version_tuple(previous):
        raise ValueError("VERSION must not decrease")
    released = [tag[1:] for tag in tags if re.fullmatch(r"v\d+\.\d+\.\d+", tag)]
    if any(current < version_tuple(v) for v in released):
        raise ValueError("VERSION is older than an existing release tag")
    if previous and version != previous and "v" + version in tags:
        raise ValueError("The requested new version is already tagged")


def main():
    version = read_version(ROOT)
    release_notes((ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), version)
    previous = None
    base = os.environ.get("CI_BASE_SHA", "")
    if base and base != "0" * 40:
        if not re.fullmatch(r"[0-9a-f]{40}", base):
            raise ValueError("Invalid base commit")
        subprocess.run(["git", "cat-file", "-e", base + "^{commit}"], check=True)
        result = subprocess.run(["git", "show", base + ":reservation-service/VERSION"], capture_output=True, text=True)
        if result.returncode == 0:
            previous = result.stdout.strip()
    tags = subprocess.check_output(["git", "tag", "--list", "v*"], text=True).splitlines()
    validate_progression(version, previous, tags)
    print("Release metadata valid:", version)
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write("version=" + version + "\n")


if __name__ == "__main__":
    main()
