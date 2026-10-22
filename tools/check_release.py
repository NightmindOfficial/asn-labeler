"""Fail CI on invalid versions, version rollback or missing release notes."""
import os
from pathlib import Path
import re
import subprocess
from release_common import COMPONENTS, read_version, release_notes, version_tuple

ROOT = Path(__file__).resolve().parents[1]


def validate_progression(version, previous=None, tags=(), current_commit_tags=()):
    current = version_tuple(version)
    if previous and current < version_tuple(previous):
        raise ValueError("VERSION must not decrease")
    released = [tag[1:] for tag in tags if re.fullmatch(r"v\d+\.\d+\.\d+", tag)]
    if any(current < version_tuple(v) for v in released):
        raise ValueError("VERSION is older than an existing release tag")
    if previous and version != previous and "v" + version in tags and "v" + version not in current_commit_tags:
        raise ValueError("The requested new version is already tagged")


def needs_version_bump(component, paths):
    folder = COMPONENTS[component] + "/"
    shared = {"LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md", "LICENSE-BPAC-EXCEPTION.txt"}
    return any(path in shared or (path.startswith(folder)
        and not path.endswith(".md") and not path.startswith(folder + "tests/")
        and path != folder + "VERSION") for path in paths)


def main():
    base = os.environ.get("CI_BASE_SHA", "")
    changed = []
    if base and base != "0" * 40:
        if not re.fullmatch(r"[0-9a-f]{40}", base):
            raise ValueError("Invalid base commit")
        subprocess.run(["git", "cat-file", "-e", base + "^{commit}"], check=True)
        changed = subprocess.check_output(["git", "diff", "--name-only", base, "HEAD"], text=True).splitlines()
    tags = subprocess.check_output(["git", "tag", "--list"], text=True).splitlines()
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    outputs = []
    for component, folder in COMPONENTS.items():
        version = read_version(ROOT, component)
        release_notes((ROOT / folder / "CHANGELOG.md").read_text(encoding="utf-8"), version)
        previous = None
        if base and base != "0" * 40:
            result = subprocess.run(["git", "show", base + ":" + folder + "/VERSION"], capture_output=True, text=True)
            if result.returncode == 0:
                previous = result.stdout.strip()
        component_tags = [tag[len(component) + 1:] for tag in tags if tag.startswith(component + "-v")]
        tag = component + "-v" + version
        current_tags = []
        if tag in tags:
            target = subprocess.check_output(["git", "rev-parse", tag + "^{commit}"], text=True).strip()
            if target == head:
                current_tags = ["v" + version]
        validate_progression(version, previous, component_tags, current_tags)
        if previous == version and needs_version_bump(component, changed):
            raise ValueError(component + " changed without increasing its VERSION")
        publish = tag not in tags or (previous is not None and previous != version)
        outputs.extend([f"{component}_version={version}", f"{component}_release={str(publish).lower()}"])
        print(component, version, "release pending" if publish else "already tagged")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write("\n".join(outputs) + "\n")


if __name__ == "__main__":
    main()
