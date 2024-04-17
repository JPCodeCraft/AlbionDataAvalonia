#!/bin/bash

APP_PATH="./AFMDataClient_Linux64"
TARGET_PATH="/usr/local/bin/AFMDataCLient/AFMDataClient_Linux64"
AUTO_START_FOLDER="/etc/xdg/autostart/"

echo "Welcome to the Albion Data Client installation / update script!"
echo "Creating directory..."
sudo mkdir -p "/usr/local/bin/AFMDataCLient/"

echo "Copying the app to $TARGET_PATH..."
sudo cp "$APP_PATH" "$TARGET_PATH"

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
Comment=Albion Data Client" >afmdataclient.desktop

echo "Moving the desktop (auto start) entry to $AUTO_START_FOLDER..."
sudo mv afmdataclient.desktop $AUTO_START_FOLDER

echo "Starting the app..."
$TARGET_PATH