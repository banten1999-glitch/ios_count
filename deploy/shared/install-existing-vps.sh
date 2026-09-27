#!/usr/bin/env bash
# Installs only the separate remote-desktop stack on the reviewed VPS.
# Run from the extracted, checksum-verified server package directory as root.
set -Eeuo pipefail
umask 077
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
[[ $EUID == 0 ]] || { echo 'Run as root on the VPS.'; exit 1; }
[[ -f app/RemoteDesktop.Signaling.dll && -f compose.yml ]] || { echo 'Extract the full server package first.'; exit 1; }
for tool in docker curl openssl getent ufw; do command -v "$tool" >/dev/null; done
host_name=remote.fakihatalmawsim.com
public_ip=46.101.195.166
caddy_container=ops-caddy-1
caddy_file=/home/fbg/ops/Caddyfile
project=remote-desktop
[[ -f "$caddy_file" ]] || { echo 'Existing Caddyfile not found; stop.'; exit 1; }
docker network inspect ops_default >/dev/null
docker inspect "$caddy_container" >/dev/null
# Guard against installing on the wrong host or creating a duplicate deployment.
ip -4 addr show | grep -Fq "$public_ip/" || { echo 'Server public IP mismatch; stop.'; exit 1; }
if [[ -f .env ]] || [[ -n "$(docker ps -aq --filter label=com.docker.compose.project="$project")" ]]; then
  echo 'An installation already exists. Stop for review rather than replacing it.'; exit 1
fi
available_kb=$(awk '/^MemAvailable:/ {print $2}' /proc/meminfo)
(( available_kb >= 430000 )) || { echo 'Less than 420 MiB available; stop for memory review.'; exit 1; }
getent ahostsv4 "$host_name" | awk '{print $1}' | grep -Fxq "$public_ip" || {
  echo "DNS not ready. Create an A record: $host_name -> $public_ip (DNS only)."; exit 1;
}
if ss -H -lntu | awk '{print $5}' | grep -Eq ':(3478|491[6-9][0-9]|49200)$'; then
  echo 'A TURN port is already in use; stop.'; exit 1
fi
if grep -Fq "$host_name" "$caddy_file"; then
  echo 'Hostname is already in Caddyfile; stop for review.'; exit 1
fi
curl -fsS --max-time 15 https://ops.fakihatalmawsim.com/v1/health >/dev/null || {
  echo 'Existing application health check failed; stop before making changes.'; exit 1;
}
# Pull prebuilt runtime images, never compile .NET on this 1 GB VPS.
docker pull mcr.microsoft.com/dotnet/aspnet:8.0
docker pull coturn/coturn:4.6
key=$(openssl rand -base64 32)
turn_secret=$(openssl rand -hex 32)
cat > .env <<ENV
PUBLIC_IP=$public_ip
PUBLIC_HOST=$host_name
PROXY_NETWORK=ops_default
SESSION_TOKEN_KEY_BASE64=$key
TURN_SHARED_SECRET=$turn_secret
ENV
unset key turn_secret
chmod 600 .env
# The runtime user needs read access to binaries, but not to the secrets file.
chmod 755 . app
find app -type d -exec chmod 755 {} +
find app -type f -exec chmod 644 {} +
docker compose --env-file .env -f compose.yml config --quiet
backup="${caddy_file}.before-remote-$(date -u +%Y%m%dT%H%M%SZ)"
cp -p -- "$caddy_file" "$backup"
caddy_changed=0
rollback() {
  status=$?
  trap - ERR
  if (( caddy_changed )); then
    cat "$backup" > "$caddy_file"
    docker exec "$caddy_container" caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 || true
  fi
  docker compose --env-file .env -f compose.yml stop >/dev/null 2>&1 || true
  echo "Installation stopped. Existing Caddy config restored if changed. Backup: $backup"
  echo 'The new stack was stopped. Keep this folder for diagnostics; do not delete existing application files.'
  exit "$status"
}
trap rollback ERR
docker compose --env-file .env -f compose.yml up -d --no-build
healthy=0
for attempt in {1..20}; do
  if docker exec "$caddy_container" wget -qO- http://remote-desktop-signaling:8080/health >/dev/null 2>&1; then
    healthy=1; break
  fi
  sleep 2
done
(( healthy == 1 ))
# Check TURN stays running, not merely that its container was created.
turn_id=$(docker compose --env-file .env -f compose.yml ps -q turn)
[[ -n "$turn_id" ]]
[[ "$(docker inspect -f '{{.State.Running}}' "$turn_id")" == true ]]
[[ "$(docker inspect -f '{{.RestartCount}}' "$turn_id")" == 0 ]]
caddy_changed=1
cat >> "$caddy_file" <<CADDY

# Remote Desktop: separate site, existing sites preserved.
$host_name {
    reverse_proxy remote-desktop-signaling:8080
}
CADDY
docker exec "$caddy_container" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
docker exec "$caddy_container" caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile
ufw allow 3478/udp comment 'Remote desktop TURN'
ufw allow 3478/tcp comment 'Remote desktop TURN'
ufw allow 49160:49200/udp comment 'Remote desktop media relay'
curl -fsS --retry 10 --retry-delay 3 --retry-all-errors --max-time 10 "https://$host_name/health"
curl -fsS --max-time 15 https://ops.fakihatalmawsim.com/v1/health >/dev/null
trap - ERR
echo
printf 'Signaling URL: https://%s\n' "$host_name"
echo 'Existing application health check passed. New service health check passed.'
echo 'TURN internet traversal still needs a test between two different networks.'
echo 'If a DigitalOcean Cloud Firewall is attached, allow 3478 UDP/TCP and UDP 49160-49200 there too.'
docker stats --no-stream
