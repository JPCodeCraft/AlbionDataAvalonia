#!/bin/sh
set -eu

GROUP_NAME="afmdataclient_bpf"
APP_SUPPORT_DIR="/Library/Application Support/AFMDataClient"
HELPER_DIR="$APP_SUPPORT_DIR/ChmodBPF"
PLIST_LABEL="com.albionfreemarket.afmdataclient.chmodbpf"
PLIST_PATH="/Library/LaunchDaemons/$PLIST_LABEL.plist"
LEGACY_LOG_PATH="/var/log/afmdataclient-chmodbpf.log"

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this script with sudo:"
    echo "sudo /bin/sh \"$0\""
    exit 1
fi

launchctl bootout "system/$PLIST_LABEL" >/dev/null 2>&1 || true

for device in /dev/bpf*; do
    if [ ! -e "$device" ]; then
        continue
    fi

    device_group="$(stat -f %Sg "$device" 2>/dev/null || true)"
    if [ "$device_group" = "$GROUP_NAME" ]; then
        chgrp wheel "$device"
        chmod 600 "$device"
    fi
done

rm -f "$PLIST_PATH" "$LEGACY_LOG_PATH"
rm -rf "$HELPER_DIR"
rmdir "$APP_SUPPORT_DIR" >/dev/null 2>&1 || true

if dscl . -read "/Groups/$GROUP_NAME" >/dev/null 2>&1; then
    dseditgroup -q -o delete "$GROUP_NAME"
fi

echo "AFM Data Client packet capture permissions were removed."
echo "Your database, backups, settings, and logs in your user Library were preserved."
