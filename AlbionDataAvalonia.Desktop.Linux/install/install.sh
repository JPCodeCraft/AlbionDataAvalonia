#!/bin/bash

APP_PATH="./AFMDataClient"
TARGET_PATH="$HOME/AFMDataClient/AFMDataClient"

echo "Creating directory..."
mkdir -p "$HOME/AFMDataClient"

echo "Copying the app to home directory..."
cp "$APP_PATH" "$TARGET_PATH"

echo "Setting capabilities for the app..."
sudo setcap cap_net_admin,cap_net_raw+eip "$TARGET_PATH"

echo "Setting the app to run on system startup..."
echo "[Desktop Entry]
Type=Application
Exec=$TARGET_PATH
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name[en_US]=AFMDataClient
Name=AFMDataClient
Comment[en_US]=Albion Data Client
Comment=Albion Data Client" > afmdataclient.desktop

echo "Moving the desktop entry to /etc/xdg/autostart/..."
sudo mv afmdataclient.desktop /etc/xdg/autostart/

echo "Starting the app..."
$TARGET_PATH