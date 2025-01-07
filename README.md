# Albion Free Market Data Client

## Windows Installation

1. Navigate to the [releases](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases) section to download the installer `AFMDataClientSetup_v_x.x.x.x.exe`.
2. The application will automatically update to the latest version when available.

## Alternative Albion Data Client

![image](https://github.com/user-attachments/assets/363773f0-f9c0-497d-83b0-79d41e407113)

![image](https://github.com/user-attachments/assets/0ed06288-e2f7-4c97-b68b-59dec3042019)

![image](https://github.com/user-attachments/assets/10725c38-8e8e-45f2-825d-4a88be23ca2d)

An alternative client for [The Albion Online Data Project](https://www.albion-online-data.com/) that provides the same functionality as the official client, but with a user-friendly graphical interface and easy-to-use settings.
All the data is uploaded to The Albion Online Data Project! Albion Free Market does not host prices databases.

This software was developed by the [Albion Free Market](https://albionfreemarket.com/) developer, [JP codecraft](https://jpcodecraft.com/).

ATTENTION: this is a beta release. Use it at your own risk.

ATTENTION²: if you use this client, uninstall the official AODP one. Otherwise you'll upload everything twice.

# Features

🧰 Collects ingame market data and uploads to [The Albion Online Data Project's](https://www.albion-online-data.com/) server

📬 Collects market mail data and shows it to you, so you can get the prices you paid for / sold items

🪙 Collect all your instant buy and sell trades and stores them so you can check prices later

📡 Able to capture market data from loadout's quick buy screen, besides the usual market screens and gold screen

🚀 Launch on startup

📌 Sits on tray

🔄 Automatic, silent updates

⚙️ Settable parallelism for PoW solving and uploads

🤌 Doesn't require admin permission to run (installing NpCap still requires, thought)

⚠️ Please note that this project is currently in testing phase and not ready for production use

# Getting Started

## Windows:

1. Navigate to the [releases](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases) section to download the installer `AFMDataClientSetup_v_x.x.x.x.exe`.
2. The application will automatically update to the latest version when available.

## Linux:

### Command to download and run the installer/updater

```bash
# Create a temporary directory
TEMP_DIR=$(mktemp -d)

# Fetch the latest release download URL
DOWNLOAD_URL=$(curl -s https://api.github.com/repos/JPCodeCraft/AlbionDataAvalonia/releases/latest | jq -r '.assets[] | select(.name == "AFMDataClient_Linux64_Installer.sh") | .browser_download_url')

# Download the file to the temporary directory
curl -L -o $TEMP_DIR/AFMDataClient_Linux64_Installer.sh $DOWNLOAD_URL

# Convert Windows line endings to Unix line endings
sed -i 's/\r$//' $TEMP_DIR/AFMDataClient_Linux64_Installer.sh

# Make the file executable
chmod +x $TEMP_DIR/AFMDataClient_Linux64_Installer.sh

# Run the installer
$TEMP_DIR/AFMDataClient_Linux64_Installer.sh
```

### Command to download and run the uninstaller

```bash
# Create a temporary directory
TEMP_DIR=$(mktemp -d)

# Fetch the latest release download URL
DOWNLOAD_URL=$(curl -s https://api.github.com/repos/JPCodeCraft/AlbionDataAvalonia/releases/latest | jq -r '.assets[] | select(.name == "AFMDataClient_Linux64_Uninstaller.sh") | .browser_download_url')

# Download the file to the temporary directory
curl -L -o $TEMP_DIR/AFMDataClient_Linux64_Uninstaller.sh $DOWNLOAD_URL

# Convert Windows line endings to Unix line endings
sed -i 's/\r$//' $TEMP_DIR/AFMDataClient_Linux64_Uninstaller.sh

# Make the file executable
chmod +x $TEMP_DIR/AFMDataClient_Linux64_Uninstaller.sh

# Run the uninstaller
$TEMP_DIR/AFMDataClient_Linux64_Uninstaller.sh
```

### Manual alternative

1. Download the `AFMDataClient_Linux64_Installer.sh` installer script from the [releases](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases) section. This script will download the application, set the necessary capabilities, and configure it to launch at startup. The application file will be saved in the `~/AFMDataClient` directory.
2. To uninstall the application, download and run the `AFMDataClient_Linux64_Uninstaller.sh` script. This will reverse all changes made by the installer script.
3. Automatic updates are not currently available for Linux. To update the application, simply run the installer script again. This will download and install the latest version.
4. Before running the scripts you might need to do: `sed -i 's/\r$//'  AFMDataClient_Linux64_Uninstaller.sh` and `chmod +x AFMDataClient_Linux64_Installer.sh`.

# FAQ

### How do I make it work with ExitLag?
Just change the `Packets redirection method` to `Legacy - NDIS` and it should work. This setting is under `TOOLS` in yout ExitLag app.

![image](https://github.com/JPCodeCraft/AlbionDataAvalonia/assets/11092613/94a76ea6-6023-40df-8d6e-e816e612befe)

# Download Stats
Check out the download statistics for this project [here](https://tooomm.github.io/github-release-stats/?username=jpcodecraft&repository=AlbionDataAvalonia)
