#!/bin/bash
set -euo pipefail

PLIST="/Library/LaunchDaemons/org.afmdata.chmodbpf.plist"

if [ "${EUID:-$(id -u)}" -ne 0 ]; then
  echo "This uninstaller must be run as root (it will be invoked via an admin prompt)." >&2
  exit 1
fi

launchctl unload "$PLIST" >/dev/null 2>&1 || true
rm -f "$PLIST"

echo "Removed LaunchDaemon. To remove your user from 'access_bpf':"
echo "  sudo dseditgroup -o edit -d $SUDO_USER -t user access_bpf"
