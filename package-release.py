"""Package matching source and deployment artifacts; never include runtime data.

Copyright (c) 2026 nightmind.dev. SPDX-License-Identifier: AGPL-3.0-only
Requires Python 3 for release engineering only.
"""
from pathlib import Path
import hashlib
import runpy
import shutil
import os
import sys
from zipfile import ZIP_DEFLATED, ZIP_STORED, ZipFile

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / "tools"))
from release_common import read_version, write_manifest
VERSION = read_version(ROOT)
OUTPUT = ROOT / "artifacts" / VERSION
SERVICE = ROOT / "reservation-service"
PUBLIC_DOCUMENTS = (
    "README.md", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md",
    "docs/USAGE.md", "docs/RELEASING.md",
)


def client_source_files(root):
    """Only named public docs are distributed; local notes never follow globbing."""
    root = Path(root)
    files = [root / name for name in PUBLIC_DOCUMENTS]
    files.extend(root / name for name in (
        "LICENSE", "NOTICE", "LICENSE-BPAC-EXCEPTION.txt", "package-release.py",
        "windows-app/build.ps1", ".gitignore", ".gitattributes",
        ".github/workflows/ci.yml",
    ))
    for folder in ("src", "assets", "vendor", "tests", "tools"):
        files.extend(path for path in (root / "windows-app" / folder).rglob("*")
                     if path.is_file() and "__pycache__" not in path.parts
                     and path.suffix not in (".pyc", ".download"))
    files.extend((root / "tools").rglob("*.py"))
    return sorted(files)


def add(archive, path, base):
    if path.is_symlink() or not path.is_file():
        raise ValueError("Missing or symlinked release input: " + str(path))
    archive.write(path, path.relative_to(base).as_posix(),
                  compress_type=ZIP_STORED if path.suffix == ".zip" else ZIP_DEFLATED)


def main():
    executable = ROOT / "windows-app/dist/asn-labeler.exe"
    if not executable.is_file():
        raise SystemExit("Build the Windows client with windows-app/build.ps1 first.")
    source_zip = ROOT / "windows-app/vendor/ZXing.Net/source-v0.16.11.0.zip"
    if not source_zip.is_file() or hashlib.sha256(source_zip.read_bytes()).hexdigest() != "71645fb22540334081d74ed5bb51c65037a8a774bf5b7d505f9cf7c3c8de80bf":
        raise SystemExit("Verified ZXing source required. Run windows-app/tools/fetch-zxing-source.ps1 first.")
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name in ("LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md"):
        shutil.copyfile(ROOT / name, SERVICE / "licenses" / name)
    module = runpy.run_path(str(SERVICE / "package_source.py"))
    module["build_source"](SERVICE, SERVICE / "source.zip")
    with ZipFile(SERVICE / "source.zip") as source:
        service_files = [SERVICE / name for name in source.namelist()]
    with ZipFile(OUTPUT / ("asn-reservation-" + VERSION + "-nas.zip"), "w") as archive:
        for path in service_files:
            add(archive, path, SERVICE)
        add(archive, SERVICE / "source.zip", SERVICE)
    with ZipFile(OUTPUT / ("asn-labeler-" + VERSION + "-source.zip"), "w") as archive:
        for path in service_files:
            add(archive, path, ROOT)
        for path in client_source_files(ROOT):
            add(archive, path, ROOT)
    shutil.copyfile(executable, OUTPUT / "asn-labeler.exe")
    write_manifest(OUTPUT, VERSION, os.environ.get("GITHUB_SHA"))
    print("Release source and NAS packages written to", OUTPUT)


if __name__ == "__main__":
    main()
