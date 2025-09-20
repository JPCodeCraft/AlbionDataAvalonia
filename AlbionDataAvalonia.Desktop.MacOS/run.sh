#!/bin/bash

# Set environment variables to ensure the UI appears
export DISPLAY=:0
export HOME="$HOME"

# Ensure deterministic working directory inside the app bundle
APP_DIR="$(cd "$(dirname "$0")" && pwd)"

# Run the published binary (single-file bundle output)
exec "$APP_DIR/AFMDataClient_MacOS64" "$@"