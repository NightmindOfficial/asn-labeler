"""Package matching source and deployment artifacts; never include runtime data.

Copyright (c) 2026 nightmind.dev. SPDX-License-Identifier: AGPL-3.0-only
Requires Python 3 for release engineering only.
"""
from pathlib import Path
import argparse
import hashlib
import json
import runpy
import shutil
import os
import sys
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from release_common import asset_names, read_version, write_manifest, version_tuple
SERVICE = ROOT / "reservation-service"
PUBLIC_DOCUMENTS = (
    "README.md", "CONTRIBUTING.md", "SECURITY.md", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md",
    "docs/USAGE.md", "docs/CLI.md", "docs/RELEASING.md",
    "windows-app/CHANGELOG.md",
)


def client_source_files(root):
    """Only named public docs are distributed; local notes never follow globbing."""
    root = Path(root)
    files = [root / name for name in PUBLIC_DOCUMENTS]
    files.append(root / "security/policy.json")
    files.extend(root / name for name in (
        "LICENSE", "NOTICE", "LICENSE-BPAC-EXCEPTION.txt", "package-release.py",
        "windows-app/build.ps1", "windows-app/build-installer.ps1", "windows-app/VERSION", ".gitignore", ".gitattributes",
        ".github/workflows/ci.yml",
    ))
    for folder in ("src", "assets", "vendor", "tests", "tools", "installer"):
        files.extend(path for path in (root / "windows-app" / folder).rglob("*")
                     if path.is_file() and "__pycache__" not in path.parts
                     and path.suffix not in (".pyc", ".download"))
    files.extend((root / "tools").rglob("*.py"))
    files.extend((root / "tools").rglob("*.cjs"))
    files.extend((root / "tools").rglob("*.ps1"))
    files.extend((root / "docs" / "images").glob("*.png"))
    return sorted(files)


def add(archive, path, base):
    if path.is_symlink() or not path.is_file():
        raise ValueError("Missing or symlinked release input: " + str(path))
    archive.write(path, path.relative_to(base).as_posix(),
                  compress_type=ZIP_STORED if path.suffix == ".zip" else ZIP_DEFLATED)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("component", choices=("client", "server"))
    component = parser.parse_args().component
    version = read_version(ROOT, component)
    output = ROOT / "artifacts" / component / version
    executable = ROOT / "windows-app/dist" / asset_names(version, "client")[0]
    if component == "client" and not executable.is_file():
        raise SystemExit("Build the client and installer with windows-app/build.ps1 and build-installer.ps1 first.")
    source_zip = ROOT / "windows-app/vendor/ZXing.Net/source-v0.16.11.0.zip"
    if component == "client" and (not source_zip.is_file() or hashlib.sha256(source_zip.read_bytes()).hexdigest() != "71645fb22540334081d74ed5bb51c65037a8a774bf5b7d505f9cf7c3c8de80bf"):
        raise SystemExit("Verified ZXing source required. Run windows-app/tools/fetch-zxing-source.ps1 first.")
    output.mkdir(parents=True, exist_ok=True)
    for name in ("LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md"):
        shutil.copyfile(ROOT / name, SERVICE / "licenses" / name)
    module = runpy.run_path(str(SERVICE / "package_source.py"))
    module["build_source"](SERVICE, SERVICE / "source.zip")
    with ZipFile(SERVICE / "source.zip") as source:
        service_files = [SERVICE / name for name in source.namelist()]
    if component == "server":
        policy = json.loads((SERVICE / "release-policy.json").read_text(encoding="utf-8"))
        if (policy.get("schema") != 1 or policy.get("version") != version or
                version_tuple(policy["minimumSafeServerVersion"]) > version_tuple(version)):
            raise ValueError("Release safety policy does not match server version")
        shutil.copyfile(SERVICE / "release-policy.json", output / "server-update-policy.json")
        with ZipFile(output / asset_names(version, "server")[0], "w") as archive:
            for path in service_files:
                add(archive, path, SERVICE)
            add(archive, SERVICE / "source.zip", SERVICE)
    else:
        with ZipFile(output / ("asn-labeler-" + version + "-source.zip"), "w") as archive:
            for path in service_files:
                add(archive, path, ROOT)
            for path in client_source_files(ROOT):
                add(archive, path, ROOT)
        shutil.copyfile(executable, output / executable.name)
    write_manifest(output, version, os.environ.get("GITHUB_SHA"), component)
    print(component, "release packages written to", output)


if __name__ == "__main__":
    main()
