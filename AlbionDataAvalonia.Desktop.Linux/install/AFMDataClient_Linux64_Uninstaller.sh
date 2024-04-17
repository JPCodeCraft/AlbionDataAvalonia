#!/bin/bash

TARGET_FOLDER="$HOME/AFMDataClient/"
AUTO_START_FOLDER="/etc/xdg/autostart/"
AUTO_START_FILE="afmdataclient.desktop"
LOCAL_APP_DATA_FOLDER="$HOME/.local/share/AFMDataClient"
APP_NAME="AFMDataClient_Linux64"

echo "Welcome to the Albion Data Client uninstallation script!"

echo "Killing the current process if it's running..."
for pid in $(pgrep -f $APP_NAME | grep -v $$); do
    kill -9 $pid
done

echo "Removing the app from system startup ($AUTO_START_FOLDER$AUTO_START_FILE)..."
sudo rm "$AUTO_START_FOLDER$AUTO_START_FILE"

echo "Removing the app data from $LOCAL_APP_DATA_FOLDER..."
rm -rf "$LOCAL_APP_DATA_FOLDER"

echo "Removing the directory $TARGET_FOLDER..."
rm -rf "$TARGET_FOLDER"

echo "Uninstallation completed!"

echo "Press any key to exit..."
read -n1 -s