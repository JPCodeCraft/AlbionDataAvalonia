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
    kill -9 $pid 2>/dev/null || true
done

echo "Creating directory $TARGET_FOLDER..."
mkdir -p "$TARGET_FOLDER"

echo "Installing curl if it's not already installed..."
if command -v apt-get &> /dev/null; then
    sudo apt-get install -y curl
elif command -v dnf &> /dev/null; then
    sudo dnf install -y curl
elif command -v yum &> /dev/null; then
    sudo yum install -y curl
elif command -v pacman &> /dev/null; then
    sudo pacman -S --noconfirm curl
fi

echo "Downloading the latest version of the app..."
if command -v jq &> /dev/null; then
    DOWNLOAD_URL=$(curl -s $REPO_API_URL | jq -r ".assets[] | select(.name == \"$FILE_NAME\") | .browser_download_url")
else
    DOWNLOAD_URL=$(curl -s $REPO_API_URL | grep -o "\"browser_download_url\": \"[^\"]*$FILE_NAME[^\"]*\"" | head -1 | cut -d '"' -f 4)
fi

if [ -z "$DOWNLOAD_URL" ] || [ "$DOWNLOAD_URL" = "null" ]; then
    echo "Error: Could not find download URL for $FILE_NAME"
    exit 1
fi

TEMP_FILE="${FULL_PATH}.tmp"

if command -v wget &> /dev/null; then
    wget --show-progress -O "$TEMP_FILE" "$DOWNLOAD_URL"
else
    curl -L --progress-bar -o "$TEMP_FILE" "$DOWNLOAD_URL"
fi

if [ ! -f "$TEMP_FILE" ] || [ ! -s "$TEMP_FILE" ]; then
    echo "Error: Download failed or file is empty"
    exit 1
fi

mv "$TEMP_FILE" "$FULL_PATH"

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
nohup "$FULL_PATH" > /dev/null 2>&1 &

read -p "Press any key to continue . . ."