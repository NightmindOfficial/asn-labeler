"""Build the public source archive from an explicit, secret-free allowlist.

Copyright (c) 2026 nightmind.dev. SPDX-License-Identifier: AGPL-3.0-only
"""
import argparse
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

FILES = (
    "app.py", "admin.py", "package_source.py", "Dockerfile", "docker-compose.yml",
    "requirements.txt", ".env.example", ".dockerignore", "README.md", "VERSION",
)
DIRECTORIES = {"web": {".html", ".js", ".css", ".ico", ".ttf", ".txt"},
               "tests": {".py"}, "licenses": {".txt", ".md", ""}}


def build_source(root, output):
    root, output = Path(root).resolve(), Path(output).resolve()
    files = [root / name for name in FILES]
    for directory, suffixes in DIRECTORIES.items():
        files.extend(path for path in (root / directory).iterdir()
                     if path.is_file() and path.suffix in suffixes)
    with ZipFile(output, "w", ZIP_DEFLATED) as archive:
        for path in sorted(files):
            if path.is_symlink() or not path.is_file():
                raise ValueError("Missing or symlinked source file: " + str(path))
            archive.write(path, path.relative_to(root).as_posix())


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default="source.zip")
    args = parser.parse_args()
    build_source(Path(__file__).parent, args.output)
