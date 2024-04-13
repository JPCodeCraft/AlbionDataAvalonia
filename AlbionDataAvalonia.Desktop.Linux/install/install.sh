#!/bin/bash

APP_PATH="./AFMDataClient"
TARGET_PATH="/usr/local/bin/AFMDataClient"

# Copy the app to /usr/local/bin
sudo cp "$APP_PATH" "$TARGET_PATH"

# Set capabilities for the app to be able to use raw sockets
sudo setcap cap_net_admin,cap_net_raw+eip "$TARGET_PATH"

# Set the app to run on system startup
echo "[Desktop Entry]
Type=Application
Exec=$TARGET_PATH
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name[en_US]=AFMDataClient
Name=AFMDataClient
Comment[en_US]=
Comment=" > afmdataclient.desktop

sudo mv afmdataclient.desktop /etc/xdg/autostart/

$TARGET_PATH