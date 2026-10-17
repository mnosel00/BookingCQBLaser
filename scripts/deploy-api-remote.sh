#!/usr/bin/env bash
# Runs ON the Oracle box (piped in over SSH by the deploy workflow). Loads the image CI just
# shipped and swaps the running API container, keeping the previous image as a rollback and
# restoring it automatically if the new container fails to come up.
set -euo pipefail

APP_DIR=/home/ubuntu/BookingCQBLaser
IMAGE_TAR=/home/ubuntu/image.tar.gz
CRED_JSON="$APP_DIR/lasertagbooking-49d070d05afa.json"
IMAGE=comboarena-api:latest
CONTAINER=api-produkcja

run_container() {
  docker run -d --name "$CONTAINER" \
    -p 127.0.0.1:8080:8080 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    --env-file "$APP_DIR/.env" \
    -v "$CRED_JSON:/app/lasertagbooking-49d070d05afa.json" \
    --restart unless-stopped \
    "$IMAGE"
}

# Preserve the current image as a rollback target before loading the new one.
docker tag "$IMAGE" comboarena-api:prev 2>/dev/null || true

echo "Loading new image..."
gunzip -c "$IMAGE_TAR" | docker load

echo "Swapping container..."
docker stop "$CONTAINER" 2>/dev/null || true
docker rm "$CONTAINER" 2>/dev/null || true
run_container

sleep 6
if [ "$(docker inspect -f '{{.State.Running}}' "$CONTAINER" 2>/dev/null)" != "true" ]; then
  echo "!! New container is not running - rolling back to comboarena-api:prev"
  docker logs --tail 40 "$CONTAINER" 2>&1 || true
  docker rm -f "$CONTAINER" 2>/dev/null || true
  docker tag comboarena-api:prev "$IMAGE"
  run_container
  rm -f "$IMAGE_TAR"
  exit 1
fi

sleep 2
code=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/api/packages || echo 000)
echo "Health check /api/packages -> HTTP $code"

rm -f "$IMAGE_TAR"
docker image prune -f >/dev/null 2>&1 || true
echo "Deploy complete."
