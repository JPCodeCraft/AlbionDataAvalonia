#!/bin/bash

APP_PATH="./AFMDataClient_Linux64"
TARGET_FOLDER="$HOME/AFMDataClient/"
FILE_NAME="AFMDataClient_Linux64"
FULL_PATH="$TARGET_FOLDER$FILE_NAME"
AUTO_START_FOLDER="/etc/xdg/autostart/"
AUTO_START_FILE="afmdataclient.desktop"
APP_NAME="AFMDataClient_Linux64"

echo "Welcome to the Albion Data Client installation / update script!"

echo "Killing the current process if it's running..."
for pid in $(pgrep -f $APP_NAME | grep -v $$); do
    kill -9 $pid
done

echo "Creating directory $TARGET_FOLDER..."
mkdir -p "$TARGET_FOLDER"

echo "Copying the app to $FULL_PATH..."
fuser -k "$FULL_PATH" || true
cp "$APP_PATH" "$FULL_PATH"

echo "Setting capabilities for the app..."
sudo setcap cap_net_admin,cap_net_raw+eip "$FULL_PATH"

echo "Setting the app to run on system startup..."
echo "[Desktop Entry]
Type=Application
Exec=$FULL_PATH
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
Name[en_US]=AFMDataClient
Name=AFMDataClient
Comment[en_US]=Albion Data Client
Comment=Albion Data Client" >$AUTO_START_FILE

echo "Moving the desktop (auto start) entry to $AUTO_START_FOLDER..."
sudo mv $AUTO_START_FILE $AUTO_START_FOLDER

echo "Starting the app..."
$FULL_PATH