"""Validate the exact release payload before uploading or publishing."""
import io
import os
from pathlib import Path, PurePosixPath
import sys
from zipfile import ZipFile
from release_common import read_version, verify_manifest

ROOT = Path(__file__).resolve().parents[1]


def validate_archive(archive, required):
    names = archive.namelist()
    if len(names) != len(set(names)) or not set(required).issubset(names):
        raise ValueError("Archive has duplicate entries or missing build inputs")
    for name in names:
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name:
            raise ValueError("Unsafe archive path")
        if any(p in (".local", ".git", ".venv", "__pycache__") for p in path.parts):
            raise ValueError("Private/build directory in archive")
        if path.name == ".env" or (path.name.startswith(".env.") and path.name != ".env.example") or ".sqlite" in path.name:
            raise ValueError("Runtime data in archive")
        if path.suffix.lower() in (".pfx", ".p12", ".pem", ".key"):
            raise ValueError("Credential file in archive")
    if archive.testzip() is not None:
        raise ValueError("Archive CRC validation failed")


def verify(directory, version, commit):
    directory = Path(directory)
    names = verify_manifest(directory, version, commit)
    if (directory / "asn-labeler.exe").read_bytes()[:2] != b"MZ":
        raise ValueError("Client is not a Windows executable")
    with ZipFile(directory / f"asn-reservation-{version}-nas.zip") as archive:
        validate_archive(archive, ["VERSION", "Dockerfile", "app.py", "README.md", "source.zip", "licenses/LICENSE"])
        if archive.read("VERSION").decode().strip() != version:
            raise ValueError("NAS package version mismatch")
        with ZipFile(io.BytesIO(archive.read("source.zip"))) as source:
            validate_archive(source, ["VERSION", "Dockerfile", "package_source.py", "app.py", "licenses/LICENSE"])
            for name in source.namelist():
                if source.read(name) != archive.read(name):
                    raise ValueError("Service source download differs from deployment: " + name)
    with ZipFile(directory / f"asn-labeler-{version}-source.zip") as archive:
        validate_archive(archive, ["LICENSE", "LICENSE-BPAC-EXCEPTION.txt", "package-release.py",
                                  ".github/workflows/ci.yml", "tools/release_common.py",
                                  "reservation-service/VERSION", "windows-app/build.ps1",
                                  "windows-app/vendor/ZXing.Net/source-v0.16.11.0.zip"])
        if archive.read("reservation-service/VERSION").decode().strip() != version:
            raise ValueError("Source package version mismatch")
    print("Verified release artifacts for", version, commit)
    return names


if __name__ == "__main__":
    verify(Path(sys.argv[1]), read_version(ROOT), os.environ.get("GITHUB_SHA"))
