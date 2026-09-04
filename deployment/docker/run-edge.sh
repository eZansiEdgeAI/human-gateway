#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# run-edge.sh — build and run the HumanGateway Edge Gateway on a Raspberry Pi
# (arm64) or old PC (amd64), using Docker or Podman (LOCAL-EDGE-1.7, EDGE-FR-01).
#
# The Edge is a LAN-only service: it serves PWA clients with no Internet and
# queues everything for later sync to the Relay. This script:
#   1. picks the container runtime (podman preferred, then docker),
#   2. builds the image natively for the local architecture (or pulls a prebuilt
#      image if HG_IMAGE points at a registry and HG_PULL=1),
#   3. starts a detached, auto-restarting container with durable state in a
#      named volume (or a bind-mounted directory via HG_DATA_DIR), and
#   4. prints the health check.
#
# Configuration (environment variables, all optional):
#   HG_CONTAINER   container runtime to use ("podman" or "docker"); auto-detected
#                  when unset.
#   HG_IMAGE       image name/ref; default "humangateway-edge:latest". Point this
#                  at a registry ref (e.g. ghcr.io/org/humangateway-edge:latest)
#                  and set HG_PULL=1 to pull a prebuilt image instead of building.
#   HG_PULL        set to 1 to pull HG_IMAGE instead of building it locally.
#   HG_VOLUME      named volume for durable state (SQLite + artifacts); default
#                  "humangateway-edge-data". Ignored when HG_DATA_DIR is set.
#   HG_DATA_DIR    bind-mount a host directory instead of the named volume
#                  (advanced: use this to control where data lives on disk).
#   HG_PORT        host port published to the container's 8080; default 8080.
#   HG_GATEWAY_ID  gateway identity (AUTH-FR-01); default "edge:local".
#   HG_RELAY_URL   Relay base URL for outbound registration/sync; optional.
#   HG_NAME        container name; default "humangateway-edge".
#
# The container runs as the unprivileged `app` user (uid 1654). A named volume
# inherits that ownership automatically, so it works with no setup on any
# runtime. A bind-mounted directory is chowned to uid 1654 when run as root; run
# as a regular (rootless) user, the container is started with --user 0 so the
# process maps to your account and can write a directory you own.
#
# Usage:
#   sudo ./run-edge.sh                 # fresh Pi/PC: named volume at /data
#   HG_PORT=8080 HG_GATEWAY_ID=edge:school-a ./run-edge.sh
#   HG_DATA_DIR=/srv/humangateway sudo ./run-edge.sh
# ---------------------------------------------------------------------------
set -euo pipefail

# --- runtime detection ------------------------------------------------------
detect_runtime() {
    if [[ -n "${HG_CONTAINER:-}" ]]; then
        command -v "$HG_CONTAINER" >/dev/null 2>&1 \
            || { echo "error: HG_CONTAINER=$HG_CONTAINER not found" >&2; exit 1; }
        echo "$HG_CONTAINER"
        return
    fi
    if command -v podman >/dev/null 2>&1; then
        echo "podman"
    elif command -v docker >/dev/null 2>&1; then
        echo "docker"
    else
        echo "error: neither podman nor docker found; install one (e.g. 'apt install podman')" >&2
        exit 1
    fi
}

RUNTIME="$(detect_runtime)"
IMAGE="${HG_IMAGE:-humangateway-edge:latest}"
NAME="${HG_NAME:-humangateway-edge}"
PORT="${HG_PORT:-8080}"
GATEWAY_ID="${HG_GATEWAY_ID:-edge:local}"
RELAY_URL="${HG_RELAY_URL:-}"
VOLUME="${HG_VOLUME:-humangateway-edge-data}"

echo "== HumanGateway Edge Gateway (runtime: $RUNTIME) =="
echo "   image:      $IMAGE"
echo "   host port:  $PORT"
echo "   gateway id: $GATEWAY_ID"
[[ -n "$RELAY_URL" ]] && echo "   relay url:  $RELAY_URL"

# --- image: build (native arch) or pull -------------------------------------
if [[ "${HG_PULL:-0}" == "1" ]]; then
    echo ">> Pulling $IMAGE"
    "$RUNTIME" pull "$IMAGE"
elif "$RUNTIME" image inspect "$IMAGE" >/dev/null 2>&1; then
    echo ">> Image $IMAGE already present (rebuild by removing it, or HG_PULL=1 to pull)."
else
    echo ">> Building $IMAGE for $(uname -m) ..."
    "$RUNTIME" build -f "$(dirname "$0")/Dockerfile.edge" -t "$IMAGE" "$(dirname "$0")/../.."
fi

# --- durable state: named volume or bind-mounted directory ------------------
run_args=()
data_desc="named volume '$VOLUME'"
if [[ -n "${HG_DATA_DIR:-}" ]]; then
    mkdir -p "$HG_DATA_DIR" "$HG_DATA_DIR/artifacts"
    if [[ "$(id -u)" -eq 0 ]]; then
        chown -R 1654:1654 "$HG_DATA_DIR"
    else
        # Rootless: run the process as uid 0, which maps to this account and can
        # write the directory you own.
        run_args+=(--user 0:0)
    fi
    run_args+=(-v "$HG_DATA_DIR:/data")
    data_desc="bind mount '$HG_DATA_DIR'"
else
    run_args+=(-v "$VOLUME:/data")
fi
if [[ -n "$RELAY_URL" ]]; then
    run_args+=(-e "Edge__RelayBaseUrl=$RELAY_URL" -e "Relay__BaseUrl=$RELAY_URL")
fi
echo "   data:       $data_desc"

# --- (re)start the container -------------------------------------------------
if "$RUNTIME" container inspect "$NAME" >/dev/null 2>&1; then
    echo ">> Replacing existing container '$NAME'"
    "$RUNTIME" rm -f "$NAME" >/dev/null
fi

echo ">> Starting container '$NAME'"
"$RUNTIME" run -d \
    --name "$NAME" \
    --restart unless-stopped \
    -p "${PORT}:8080" \
    -e "Edge__GatewayId=$GATEWAY_ID" \
    "${run_args[@]}" \
    "$IMAGE"

# --- health check -----------------------------------------------------------
echo ">> Waiting for the Edge to come up ..."
for _ in $(seq 1 20); do
    if command -v curl >/dev/null 2>&1 \
        && curl -fsS "http://127.0.0.1:${PORT}/healthz" >/dev/null 2>&1; then
        echo ""
        echo "Edge Gateway is running."
        echo "  Health:      http://127.0.0.1:${PORT}/healthz"
        echo "  Sync status: http://127.0.0.1:${PORT}/sync/status"
        echo "  Logs:        $RUNTIME logs -f $NAME"
        exit 0
    fi
    sleep 1
done

echo ""
echo "Edge Gateway started, but /healthz did not answer within 20s."
echo "Check logs with: $RUNTIME logs $NAME"
exit 1
