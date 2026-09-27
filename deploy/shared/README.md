# Add remote desktop to an existing VPS

Prepared template, **not deployed**. First run `bash deploy/shared/preflight.sh`
and review available memory, load, the existing Caddy network, and port ownership.
Do not run the standalone deploy/docker-compose.yml on a server whose existing
Caddy already owns 80/443.

1. Keep a backup of the current Caddyfile. Make an A record for a new hostname
   pointing to the VPS. The hostname must be confirmed before editing client settings.
2. Copy `.env.example` to `.env` in this directory. Fill PUBLIC_IP, PUBLIC_HOST,
   PROXY_NETWORK and two independently generated secrets. Do not reuse another app's secrets.
3. Confirm 3478 and UDP 49160–49200 are unused. Permit 3478 TCP/UDP and
   UDP 49160–49200 in both the provider and host firewalls. Keep SSH and existing
   app rules. Do not expose the signaling container's 8080 port publicly.
4. From the repository root validate with:
   `docker compose --env-file deploy/shared/.env -f deploy/shared/compose.yml config --quiet`
   Never paste full rendered config: it contains secrets.
5. Build the image in CI/on another machine if VPS memory is tight: Docker runtime
   resource limits do NOT limit image build memory. The application memory budget here
   is 320 MiB plus Docker and the existing apps; it is not a guarantee of safe capacity.
6. Append the adjusted Caddyfile.fragment to the existing Caddyfile, validate it with
   the existing container's Caddy command, then reload Caddy using that deployment's
   normal procedure. Do not replace the existing file or recreate the ops stack.
7. Start only this stack:
   `docker compose --env-file deploy/shared/.env -f deploy/shared/compose.yml up -d --build`
   Check `https://YOUR_HOST/health`, existing app health, and memory after startup.
8. Set Host:SignalingBaseUrl and Controller:SignalingBaseUrl to https://YOUR_HOST
   in the respective appsettings.json. Restart both apps. Test on different networks.

This conservative shared-host profile caps TURN traffic at 1,500,000 bytes/s per
session and 3,000,000 bytes/s total (approximately 12 and 24 Mbit/s); start with one
pair and measure before increasing limits. Direct peer-to-peer media avoids TURN
when available. This initial profile advertises UDP TURN; networks blocking UDP
need a separately validated TCP/TLS fallback. Media remains DTLS-SRTP encrypted;
this profile does not offer TLS on the TURN transport itself.

Rollback: stop only this Compose project, remove only its added Caddy site block,
validate and reload Caddy. Do not stop or prune unrelated containers/networks.

# Low-latency video

Capture releases GPU frame ownership before handing pixels to the encoder. A
bounded worker keeps only the newest waiting frame while encoding the current
one, and the viewer schedules only one UI render at a time. Full captured resolution
is preserved; the initial target is High (30 fps), not a promise of 30 delivered
fps or zero latency. The managed software codec, CPU, screen dimensions, network
RTT and available bitrate remain limiting factors. Bandwidth adaptation is not yet
connected to real transport measurements, so no automatic network-quality claim
is made. Session notification, tray status and disconnect controls remain visible.
