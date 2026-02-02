#!/bin/bash
set -euo pipefail

GROUP="access_bpf"
PLIST="/Library/LaunchDaemons/org.afmdata.chmodbpf.plist"

# Require root
if [ "${EUID:-$(id -u)}" -ne 0 ]; then
  echo "This installer must be run as root (it will be invoked via an admin prompt)." >&2
  exit 1
fi

# Create group if it does not exist
if ! dscl . -read "/Groups/$GROUP" >/dev/null 2>&1; then
  dseditgroup -o create -r "BPF access" "$GROUP"
fi

# Add invoking user to the group
TARGET_USER="${SUDO_USER:-$USER}"
if [ -n "$TARGET_USER" ]; then
  dseditgroup -o edit -a "$TARGET_USER" -t user "$GROUP" || true
fi

# Install LaunchDaemon to set permissions on /dev/bpf*
cat > "$PLIST" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>org.afmdata.chmodbpf</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/sh</string>
    <string>-c</string>
    <string>chgrp access_bpf /dev/bpf* && chmod g+rw /dev/bpf*</string>
  </array>
  <key>RunAtLoad</key><true/>
</dict></plist>
PLIST

chmod 644 "$PLIST"
chown root:wheel "$PLIST"
launchctl unload "$PLIST" >/dev/null 2>&1 || true
launchctl load -w "$PLIST"

# Apply immediately for current boot
if dscl . -read "/Groups/$GROUP" >/dev/null 2>&1; then
  chgrp "$GROUP" /dev/bpf* && chmod g+rw /dev/bpf* || true
fi

echo "BPF permissions configured. Please log out and log back in so group changes take effect."
