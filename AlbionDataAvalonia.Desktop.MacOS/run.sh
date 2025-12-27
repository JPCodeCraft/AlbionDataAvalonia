#!/bin/bash

# Set environment variables to ensure the UI appears
export DISPLAY=:0
export HOME="$HOME"

# Ensure deterministic working directory inside the app bundle
APP_DIR="$(cd "$(dirname "$0")" && pwd)"

# Check if we're already running as root
if [ "$EUID" -eq 0 ]; then
    # Already root - preserve user's HOME if running via sudo
    if [ -n "$SUDO_USER" ]; then
        export HOME=$(eval echo ~$SUDO_USER)
    fi
    cd "$APP_DIR"
    exec "$APP_DIR/AFMDataClient_MacOS" "$@"
else
    # Not root - prompt for admin privileges using osascript
    # This shows the native macOS password dialog
    osascript -e "do shell script \"cd '$APP_DIR' && DISPLAY=:0 HOME='$HOME' '$APP_DIR/AFMDataClient_MacOS'\" with administrator privileges"
fi
