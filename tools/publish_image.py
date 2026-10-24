"""Publish the tested Docker artifact; stable only follows a published release."""
import json
import os
import subprocess
import sys
from pathlib import Path
from publish_release import GitHub
from release_common import read_version

IMAGE = "ghcr.io/nightmindofficial/asn-reservation"


def docker(*args):
    return subprocess.check_output(["docker", *args], text=True).strip()


def main():
    mode = sys.argv[1]
    if mode not in ("version", "stable"):
        raise ValueError("Expected version or stable")
    version = read_version(Path(__file__).resolve().parents[1], "server")
    commit = os.environ["GITHUB_SHA"]
    api = GitHub(os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_TOKEN"])
    if api.request("GET", "/git/ref/heads/main")["object"]["sha"] != commit:
        print("main advanced; no image tags changed")
        return
    source = json.loads(docker("image", "inspect", "asn-reservation:ci"))[0]
    labels = source["Config"]["Labels"]
    if labels.get("org.opencontainers.image.version") != version or labels.get("org.opencontainers.image.revision") != commit:
        raise ValueError("Image does not belong to this release")
    tag = IMAGE + ":" + version
    if mode == "stable":
        release = api.request("GET", "/releases/tags/server-v" + version)
        if not release or release["draft"] or api.tag_commit("server-v" + version) != commit:
            raise ValueError("Stable requires the published matching release")
        docker("pull", tag)
        remote = json.loads(docker("image", "inspect", tag))[0]
        if remote["Id"] != source["Id"]:
            raise ValueError("Published image differs from tested image")
        docker("tag", tag, IMAGE + ":stable")
        docker("push", IMAGE + ":stable")
        return
    existing = subprocess.run(["docker", "manifest", "inspect", tag], capture_output=True, text=True)
    if existing.returncode == 0:
        docker("pull", tag)
        if json.loads(docker("image", "inspect", tag))[0]["Id"] != source["Id"]:
            raise ValueError("Version tag already exists with different contents; never overwrite it")
        return
    if not any(message in existing.stderr.lower() for message in ("manifest unknown", "no such manifest")):
        # GHCR can report DENIED rather than MANIFEST_UNKNOWN before the very
        # first package exists. Confirm absence through the authenticated API.
        owner = os.environ["GITHUB_REPOSITORY"].split("/")[0]
        account = api.request("GET", "https://api.github.com/users/" + owner)
        kind = "orgs" if account["type"] == "Organization" else "users"
        package = api.request("GET", "https://api.github.com/" + kind + "/" + owner + "/packages/container/asn-reservation")
        if package is not None:
            raise RuntimeError("Registry lookup failed; no image tag changed")
    docker("tag", source["Id"], tag)
    docker("push", tag)


if __name__ == "__main__":
    main()
