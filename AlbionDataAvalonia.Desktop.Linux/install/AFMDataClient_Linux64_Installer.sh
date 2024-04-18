#!/bin/bash

REPO_API_URL="https://api.github.com/repos/JPCodeCraft/AlbionDataAvalonia/releases/latest"
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

echo "Installing curl if it's not already installed..."
sudo apt-get install curl

echo "Downloading the latest version of the app..."
DOWNLOAD_URL=$(curl -s $REPO_API_URL | grep "browser_download_url.*$FILE_NAME" | cut -d : -f 2,3 | tr -d \" )
wget --show-progress $DOWNLOAD_URL -O "$FULL_PATH"

echo "Making the downloaded file executable..."
chmod +x "$FULL_PATH"

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

read -p "Press any key to continue . . ."