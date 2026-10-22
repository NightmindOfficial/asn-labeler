"""Check the non-root, read-only container with disposable local test data."""
import io
import json
from pathlib import Path
import secrets
import subprocess
import time
from urllib.request import urlopen
from zipfile import ZipFile
from release_common import read_version


def docker(*args, **kwargs):
    return subprocess.check_output(["docker", *args], text=True, **kwargs).strip()


def main():
    suffix = secrets.token_hex(6)
    name, volume = "asn-ci-" + suffix, "asn-ci-data-" + suffix
    try:
        docker("volume", "create", volume)
        docker("run", "--detach", "--name", name, "--read-only", "--cap-drop", "ALL",
               "--security-opt", "no-new-privileges:true", "--tmpfs", "/tmp:size=16m,mode=1777",
               "--mount", f"type=volume,source={volume},target=/data", "--publish", "127.0.0.1::8765",
               "--env", "PAPERLESS_URL=http://paperless.invalid", "--env", "PAPERLESS_TOKEN=ci-test-only",
               "--env", "API_KEYS=" + secrets.token_urlsafe(32),
               "--env", "ADMIN_API_KEYS=" + secrets.token_urlsafe(32), "asn-reservation:ci")
        address = docker("port", name, "8765/tcp").splitlines()[0]
        base = "http://" + address
        version = read_version(Path(__file__).resolve().parents[1])
        for attempt in range(30):
            try:
                with urlopen(base + "/health", timeout=2) as response:
                    health = json.load(response)
                break
            except OSError:
                if attempt == 29:
                    raise
                time.sleep(1)
        if health.get("ok") is not True or health.get("version") != version:
            raise ValueError("Container health/version mismatch")
        if docker("exec", name, "id", "-u") == "0":
            raise ValueError("Container is running as root")
        for path in ("/admin/", "/admin/license"):
            with urlopen(base + path, timeout=5) as response:
                if response.status != 200:
                    raise ValueError("Public UI/license endpoint unavailable")
        with urlopen(base + "/admin/source.zip", timeout=5) as response:
            with ZipFile(io.BytesIO(response.read())) as source:
                if source.read("VERSION").decode().strip() != version:
                    raise ValueError("Container source archive version mismatch")
        docker("exec", name, "python", "-c", "import sqlite3; c=sqlite3.connect('/data/reservations.sqlite3'); assert c.execute('PRAGMA user_version').fetchone()[0] == 3")
        print("Container health, database, permissions, admin UI and source download passed.")
    except Exception:
        subprocess.run(["docker", "logs", name], check=False)
        raise
    finally:
        subprocess.run(["docker", "rm", "--force", name], check=False, stdout=subprocess.DEVNULL)
        subprocess.run(["docker", "volume", "rm", volume], check=False, stdout=subprocess.DEVNULL)


if __name__ == "__main__":
    main()
