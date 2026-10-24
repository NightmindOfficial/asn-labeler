#!/bin/sh
# Run from the NAS project directory. No automatic updates without --apply.
set -eu
if [ "$#" -lt 1 ] || [ "$#" -gt 2 ]; then
  echo 'Usage: sh update-server.sh EXISTING_COMPOSE_PROJECT [--apply]' >&2
  exit 2
fi
project=$1
mode=${2:---check}
case "$mode" in --apply|--check) ;; *) exit 2 ;; esac
cd "$(dirname "$0")"
exec 9>.asn-update.lock
flock -n 9 || { echo 'Another updater is running.'; exit 1; }
compose() { docker compose -p "$project" -f docker-compose.registry.yml "$@"; }
container=$(compose ps -q asn-reservation)
[ -n "$container" ] || { echo 'Existing project not found; refusing to create a new data volume.'; exit 1; }
docker pull ghcr.io/nightmindofficial/asn-reservation:stable
image=$(docker image inspect ghcr.io/nightmindofficial/asn-reservation:stable --format '{{index .RepoDigests 0}}')
case "$image" in ghcr.io/nightmindofficial/asn-reservation@sha256:*) ;; *) exit 1 ;; esac
target=$(docker image inspect "$image" --format '{{index .Config.Labels "org.opencontainers.image.version"}}')
current=$(compose exec -T asn-reservation cat /opt/asn/VERSION)
echo "Installed: $current; available: $target"
[ "$mode" = --apply ] || exit 0
[ "$current" != "$target" ] || exit 0
# prepare validates the version, checks unresolved jobs, blocks new starts and
# makes a SQLite backup before replacing anything. No automatic DB rollback.
lease="$(date +%s)$$"
trap 'compose exec -T asn-reservation python maintenance.py resume --lease "$lease" || true' EXIT
trap 'exit 1' HUP INT TERM
compose exec -T asn-reservation python maintenance.py prepare --target "$target" --lease "$lease"
ASN_SERVER_IMAGE=$image
export ASN_SERVER_IMAGE
compose up -d --no-deps --pull never asn-reservation
i=0
while [ "$i" -lt 30 ]; do
  if compose exec -T asn-reservation python -c 'import json,sys,urllib.request; d=json.load(urllib.request.urlopen("http://127.0.0.1:8765/health",timeout=3)); sys.exit(0 if d.get("ok") and d.get("version")==sys.argv[1] else 1)' "$target"; then
    echo "Updated successfully to $target."
    exit 0
  fi
  i=$((i+1))
  sleep 2
done
echo 'Update health check failed. Inspect Docker logs; do not restore an older database automatically.' >&2
exit 1
