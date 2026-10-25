"""Version, release notes and artifact integrity shared by CI and packaging."""
import hashlib
import json
from pathlib import Path
import re


def version_tuple(value):
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", value):
        raise ValueError("Version must be X.Y.Z without leading zeros or prerelease suffixes")
    parts = tuple(map(int, value.split(".")))
    if max(parts) > 65534:
        raise ValueError("Version exceeds .NET Framework assembly version limits")
    return parts


COMPONENTS = {"client": "windows-app", "server": "reservation-service"}


def read_version(root, component="server"):
    version = (Path(root) / COMPONENTS[component] / "VERSION").read_text(encoding="utf-8").strip()
    version_tuple(version)
    return version


def release_notes(text, version):
    headings = list(re.finditer(r"^## (.+?)\s*$", text, re.MULTILINE))
    matches = [i for i, h in enumerate(headings) if h.group(1) == version]
    if len(matches) != 1:
        raise ValueError("CHANGELOG.md must contain exactly one '## " + version + "' section")
    index = matches[0]
    end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
    notes = text[headings[index].end():end].strip()
    if not notes:
        raise ValueError("Release notes must not be empty")
    return notes


def asset_names(version, component="client"):
    version_tuple(version)
    if component == "client":
        binary = f"asn-labeler-{version}-setup.exe" if version_tuple(version) >= (2, 0, 0) else "asn-labeler.exe"
        return [binary, f"asn-labeler-{version}-source.zip"]
    if component == "server":
        return [f"asn-reservation-{version}-nas.zip"] + (["server-update-policy.json"] if version_tuple(version) >= (2, 1, 0) else [])
    raise ValueError("Unknown component")


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_manifest(directory, version, commit, component="client"):
    directory = Path(directory)
    files = {name: {"sha256": digest(directory / name), "size": (directory / name).stat().st_size}
             for name in asset_names(version, component)}
    manifest = directory / "release-manifest.json"
    manifest.write_text(json.dumps({"component": component, "version": version, "commit": commit, "files": files}, indent=2) + "\n", encoding="utf-8")
    names = asset_names(version, component) + [manifest.name]
    (directory / "SHA256SUMS.txt").write_text("".join(digest(directory / n) + "  " + n + "\n" for n in names), encoding="utf-8")


def verify_manifest(directory, version, commit, component="client"):
    directory = Path(directory)
    manifest = json.loads((directory / "release-manifest.json").read_text(encoding="utf-8"))
    if manifest.get("component") != component or manifest.get("version") != version or manifest.get("commit") != commit:
        raise ValueError("Artifacts do not belong to this version and commit")
    if set(manifest.get("files", {})) != set(asset_names(version, component)):
        raise ValueError("Unexpected artifact manifest")
    names = asset_names(version, component) + ["release-manifest.json", "SHA256SUMS.txt"]
    if {p.name for p in directory.iterdir()} != set(names):
        raise ValueError("Release directory contains missing or unexpected files")
    for name, expected in manifest["files"].items():
        path = directory / name
        if path.is_symlink() or expected != {"sha256": digest(path), "size": path.stat().st_size}:
            raise ValueError("Artifact integrity check failed: " + name)
    checksums = "".join(digest(directory / n) + "  " + n + "\n" for n in names[:-1])
    if (directory / "SHA256SUMS.txt").read_text(encoding="utf-8") != checksums:
        raise ValueError("Checksum list differs from artifacts")
    return names
