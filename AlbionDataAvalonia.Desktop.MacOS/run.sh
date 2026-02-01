#!/bin/bash

# Set environment variables to ensure the UI appears
export DISPLAY=:0
export HOME="$HOME"

# Ensure deterministic working directory inside the app bundle
APP_DIR="$(cd "$(dirname "$0")" && pwd)"
BIN="$APP_DIR/AFMDataClient_MacOS"

# Run without admin; BPF permissions are handled via access_bpf LaunchDaemon
# Expose bundle directory to the app so it can find bundled scripts
export AFM_APP_DIR="$APP_DIR"
cd "$APP_DIR"
exec "$BIN" "$@"
