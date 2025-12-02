#!/bin/bash

# Set environment variables to ensure the UI appears
export DISPLAY=:0
export HOME="$HOME"

# Ensure deterministic working directory inside the app bundle
APP_DIR="$(cd "$(dirname "$0")" && pwd)"

# Change to the app directory so that relative paths work correctly
# This ensures DefaultAppSettings.json and DefaultUserSettings.json can be found
cd "$APP_DIR"

# Run the published binary (single-file bundle output)
exec "$APP_DIR/AFMDataClient_MacOS64" "$@"