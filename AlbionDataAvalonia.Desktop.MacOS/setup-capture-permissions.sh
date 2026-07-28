#!/bin/sh
set -eu

GROUP_NAME="afmdataclient_bpf"
GROUP_REAL_NAME="AFM Data Client packet capture"
APP_SUPPORT_DIR="/Library/Application Support/AFMDataClient"
HELPER_DIR="/Library/Application Support/AFMDataClient/ChmodBPF"
HELPER_PATH="$HELPER_DIR/chmod-bpf.sh"
PLIST_LABEL="com.albionfreemarket.afmdataclient.chmodbpf"
PLIST_PATH="/Library/LaunchDaemons/$PLIST_LABEL.plist"
LEGACY_LOG_PATH="/var/log/afmdataclient-chmodbpf.log"

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this script with sudo:"
    echo "sudo /bin/sh \"$0\""
    exit 1
fi

TARGET_USER="${SUDO_USER:-}"
if [ -z "$TARGET_USER" ] || [ "$TARGET_USER" = "root" ]; then
    TARGET_USER="$(stat -f %Su /dev/console 2>/dev/null || true)"
fi

if [ -z "$TARGET_USER" ] || [ "$TARGET_USER" = "root" ]; then
    echo "Unable to determine the logged-in user to grant packet capture access."
    exit 1
fi

if ! dscl . -read "/Groups/$GROUP_NAME" >/dev/null 2>&1; then
    dseditgroup -o create -r "$GROUP_REAL_NAME" "$GROUP_NAME"
fi

if ! dseditgroup -o checkmember -m "$TARGET_USER" "$GROUP_NAME" >/dev/null 2>&1; then
    dseditgroup -o edit -a "$TARGET_USER" -t user "$GROUP_NAME"
fi

launchctl bootout "system/$PLIST_LABEL" >/dev/null 2>&1 || true

mkdir -p "$HELPER_DIR"
chown root:wheel "$APP_SUPPORT_DIR" "$HELPER_DIR"
chmod 755 "$APP_SUPPORT_DIR" "$HELPER_DIR"

cat > "$HELPER_PATH" <<'SCRIPT'
#!/bin/zsh

GROUP_NAME="afmdataclient_bpf"
FORCE_CREATE_BPF_MAX=256

if ! dscl . -read "/Groups/$GROUP_NAME" >/dev/null 2>&1; then
    exit 0
fi

sysctl_max="$(sysctl -n debug.bpf_maxdevices 2>/dev/null || echo 0)"
case "$sysctl_max" in
    ''|*[!0-9]*)
        sysctl_max=0
        ;;
esac

if [ "$FORCE_CREATE_BPF_MAX" -gt "$sysctl_max" ]; then
    FORCE_CREATE_BPF_MAX="$sysctl_max"
fi

current_device=0
while [ "$current_device" -lt "$FORCE_CREATE_BPF_MAX" ]; do
    read -r -n 0 < "/dev/bpf$current_device" >/dev/null 2>&1 || true
    current_device=$((current_device + 1))
done

setopt NULL_GLOB
helper_status=0
for device in /dev/bpf*; do
    chgrp "$GROUP_NAME" "$device" 2>/dev/null || helper_status=1
    chmod g+rw "$device" 2>/dev/null || helper_status=1
done

exit "$helper_status"
SCRIPT

chown root:wheel "$HELPER_PATH"
chmod 755 "$HELPER_PATH"

cat > "$PLIST_PATH" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>$PLIST_LABEL</string>
    <key>ProgramArguments</key>
    <array>
        <string>$HELPER_PATH</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
</dict>
</plist>
EOF

chown root:wheel "$PLIST_PATH"
chmod 644 "$PLIST_PATH"
rm -f "$LEGACY_LOG_PATH"

if ! launchctl bootstrap system "$PLIST_PATH" >/dev/null 2>&1; then
    echo "Unable to load the packet capture permission service."
    exit 1
fi

echo "Packet capture permissions were installed for user '$TARGET_USER'."
echo "Restart AFM Data Client. If capture is still denied, log out and back in or reboot."
