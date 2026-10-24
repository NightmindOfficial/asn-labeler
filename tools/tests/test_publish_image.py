import json
import os
import sys
import unittest
from unittest import mock
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parents[1]))
import publish_image as module


class ImagePublicationTests(unittest.TestCase):
    def execute(self, mode="version", remote_id="sha256:tested", exists=True, released=True, current=True):
        calls = []
        def docker(*args):
            calls.append(args)
            if args[:2] == ("image", "inspect"):
                identifier = "sha256:tested" if args[2] == "asn-reservation:ci" else remote_id
                return json.dumps([{"Id": identifier, "Config": {"Labels": {
                    "org.opencontainers.image.version": "2.1.0", "org.opencontainers.image.revision": "commit"}}}])
            return ""
        api = mock.Mock()
        api.request.side_effect = lambda method, path: ({"object": {"sha": "commit" if current else "other"}}
            if path == "/git/ref/heads/main" else {"draft": False} if released else None)
        api.tag_commit.return_value = "commit"
        with mock.patch.dict(os.environ, {"GITHUB_SHA": "commit", "GITHUB_TOKEN": "test", "GITHUB_REPOSITORY": "NightmindOfficial/asn-labeler"}), \
             mock.patch.object(sys, "argv", ["publish_image.py", mode]), \
             mock.patch.object(module, "GitHub", return_value=api), \
             mock.patch.object(module, "read_version", return_value="2.1.0"), \
             mock.patch.object(module, "docker", side_effect=docker), \
             mock.patch.object(module.subprocess, "run", return_value=mock.Mock(returncode=0 if exists else 1, stderr="manifest unknown")):
            module.main()
        return calls

    def test_new_version_pushes_only_immutable_tag(self):
        calls = self.execute(exists=False)
        self.assertIn(("push", module.IMAGE + ":2.1.0"), calls)
        self.assertNotIn(("push", module.IMAGE + ":stable"), calls)

    def test_existing_different_image_is_not_overwritten(self):
        with self.assertRaises(ValueError):
            self.execute(remote_id="sha256:other")

    def test_existing_identical_image_is_idempotent(self):
        self.assertFalse(any(call[0] == "push" for call in self.execute()))

    def test_superseded_workflow_does_not_publish(self):
        self.assertEqual(self.execute(current=False), [])

    def test_stable_requires_published_release_and_matching_image(self):
        with self.assertRaises(ValueError):
            self.execute(mode="stable", released=False)
        with self.assertRaises(ValueError):
            self.execute(mode="stable", remote_id="sha256:other")
        self.assertIn(("push", module.IMAGE + ":stable"), self.execute(mode="stable"))
