"""Signed, release-independent safety floors. No credentials are transmitted."""
import base64
import hashlib
import hmac
import json
from pathlib import Path

POLICY_URL = "https://raw.githubusercontent.com/NightmindOfficial/asn-labeler/main/security/policy.json"


def verify(envelope):
    from server_updates import version
    key = json.loads(Path(__file__).with_name("policy-key.json").read_text())
    payload = base64.b64decode(envelope["payload"], validate=True)
    signature = base64.b64decode(envelope["signature"], validate=True)
    modulus = base64.b64decode(key["modulus"], validate=True)
    n = int.from_bytes(modulus, "big")
    e = int.from_bytes(base64.b64decode(key["exponent"], validate=True), "big")
    if len(payload) > 16384 or len(signature) != len(modulus) or int.from_bytes(signature, "big") >= n:
        raise ValueError("Invalid security policy signature")
    # RSASSA-PKCS1-v1_5 with SHA-256: compare the complete encoded message.
    digest = bytes.fromhex("3031300d060960864801650304020105000420") + hashlib.sha256(payload).digest()
    expected = b"\x00\x01" + b"\xff" * (len(modulus) - len(digest) - 3) + b"\x00" + digest
    actual = pow(int.from_bytes(signature, "big"), e, n).to_bytes(len(modulus), "big")
    if not hmac.compare_digest(actual, expected):
        raise ValueError("Invalid security policy signature")
    policy = json.loads(payload)
    if policy.get("schema") != 1 or type(policy.get("revision")) is not int or not 0 < policy["revision"] <= 2147483647:
        raise ValueError("Invalid security policy schema")
    for component in ("Client", "Server"):
        version(policy["minimum" + component + "Version"])
    if not isinstance(policy.get("reason"), str) or len(policy["reason"]) > 1000:
        raise ValueError("Invalid security policy reason")
    return policy


def fetch_policy(fetch):
    policy = verify(fetch(POLICY_URL))
    # Do not activate a floor before its escape route has been published.
    for component in ("client", "server"):
        minimum = policy["minimum" + component.title() + "Version"]
        release = fetch("https://api.github.com/repos/NightmindOfficial/asn-labeler/releases/tags/" + component + "-v" + minimum)
        if release.get("draft") or release.get("prerelease") or release.get("tag_name") != component + "-v" + minimum:
            raise ValueError("Required release is not available")
        if component == "client" and not any(a.get("name") == "asn-labeler-" + minimum + "-setup.exe" for a in release.get("assets", [])):
            raise ValueError("Required installer is not available")
    return policy
