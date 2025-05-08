#!/bin/bash

# Set environment variables to ensure the UI appears
export DISPLAY=:0
export HOME="$HOME"

# Run the actual executable with elevated privileges
exec "$(dirname "$0")/Contents/MacOS/AlbionDataAvalonia" "$@"