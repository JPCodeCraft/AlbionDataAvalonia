#!/bin/sh
set -eu

GROUP_NAME="afmdataclient_bpf"
GROUP_REAL_NAME="AFM Data Client packet capture"
HELPER_DIR="/Library/Application Support/AFMDataClient/ChmodBPF"
HELPER_PATH="$HELPER_DIR/chmod-bpf.sh"
PLIST_LABEL="com.albionfreemarket.afmdataclient.chmodbpf"
PLIST_PATH="/Library/LaunchDaemons/$PLIST_LABEL.plist"

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

mkdir -p "$HELPER_DIR"

cat > "$HELPER_PATH" <<'SCRIPT'
#!/bin/sh

GROUP_NAME="afmdataclient_bpf"

if ! dscl . -read "/Groups/$GROUP_NAME" >/dev/null 2>&1; then
    exit 0
fi

for device in /dev/bpf*; do
    if [ -e "$device" ]; then
        chgrp "$GROUP_NAME" "$device" 2>/dev/null || true
        chmod g+rw "$device" 2>/dev/null || true
    fi
done

exit 0
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
    <key>StartInterval</key>
    <integer>60</integer>
    <key>StandardOutPath</key>
    <string>/var/log/afmdataclient-chmodbpf.log</string>
    <key>StandardErrorPath</key>
    <string>/var/log/afmdataclient-chmodbpf.log</string>
</dict>
</plist>
EOF

chown root:wheel "$PLIST_PATH"
chmod 644 "$PLIST_PATH"

launchctl bootout system "$PLIST_PATH" >/dev/null 2>&1 || true
if ! launchctl bootstrap system "$PLIST_PATH" >/dev/null 2>&1; then
    launchctl load -w "$PLIST_PATH" >/dev/null 2>&1 || true
fi

"$HELPER_PATH"

echo "Packet capture permissions were installed for user '$TARGET_USER'."
echo "Restart AFM Data Client. If capture is still denied, log out and back in or reboot."
