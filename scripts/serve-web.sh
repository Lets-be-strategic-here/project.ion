#!/usr/bin/env bash
# Serves the local Web build (Build/Web) at http://localhost:${PORT:-8080}/
# The build uses Compression: Disabled, so no special headers are needed.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIR="${1:-$ROOT/Build/Web}"
PORT="${PORT:-8080}"
if [ ! -f "$DIR/index.html" ]; then
  echo "No build at $DIR (missing index.html). Build first: see docs/BUILD.md" >&2
  exit 1
fi
echo "Serving $DIR at http://localhost:$PORT/  (Ctrl+C to stop)"
exec python3 -m http.server "$PORT" --bind 127.0.0.1 --directory "$DIR"
