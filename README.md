# Albion Free Market Data Client

[![Release](https://img.shields.io/github/v/release/JPCodeCraft/AlbionDataAvalonia)](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases)
[![Downloads](https://img.shields.io/github/downloads/JPCodeCraft/AlbionDataAvalonia/total)](https://tooomm.github.io/github-release-stats/?username=jpcodecraft&repository=AlbionDataAvalonia)

## ⚠️ ATTENTION

After instalation, the Data Client will be minimized in system tray!

![image](https://github.com/user-attachments/assets/7093690e-5735-4636-bc69-7d00e57f7d57)


## 💻 Data Client

An alternative client for [The Albion Online Data Project](https://www.albion-online-data.com/) that provides the same functionality as the official client, but with a user-friendly graphical interface and easy-to-use settings.

> **Note**: Data is mostly uploaded to The Albion Online Data Project, except when the user explicitly chooses not to.

> **Beta Release**: This software is in beta. Use at your own risk.

> **Important**: If you use this client, uninstall the official AODP client to avoid uploading data twice.

> **Free and Open Source**: This software is completely free, open source (check license) and is not tied to the usage of Albion Free Market website. The data uploaded via this software to AODP can be seen in any Albion Online fansite that consumes AODP and even on your own sheets.

![Main Interface](https://github.com/user-attachments/assets/ba7d2d33-9e80-49b2-aeae-6173892de15e)


## 🚀 Features

| Feature                   | Description                                                                    |
| ------------------------- | ------------------------------------------------------------------------------ |
| 🧰 Market Data Collection | Captures in-game market data and uploads to AODP servers                       |
| 📬 Market Mail Tracking   | Collects and displays your market mail data for price tracking                 |
| 🪙 Trade History          | Stores instant buy/sell trades for later reference                             |
| 📡 Enhanced Capture       | Captures data from loadout's quick buy screen, market screens, and gold screen |
| 🚀 Launch on Startup      | Automatically starts with your system                                          |
| 📌 System Tray            | Runs quietly in system tray for minimal interference                           |
| 🔄 Auto-Updates           | Silent, automatic updates                                                      |
| ⚙️ Configurable Settings  | Adjustable parallelism for AODP's PoW solving and uploads                      |
| 🤌 User-Friendly          | No admin permissions required (except for NpCap installation)                  |
| 🌐 Free and Open Source   | This software is free to use and is open source                                |

## 📥 Installation

### Windows

1. Navigate to the [releases](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases) section to download the latest installer (`AFMDataClientSetup_v_x.x.x.x.exe`)
2. Run the installer and follow the on-screen instructions
3. After installation, the application will run minimized in the system tray
4. Click the tray icon to open the user interface
5. The application will automatically update when new versions are available

**Uninstallation**

Use the regular Windows `Uninstall a program` feature to remove the application.

### Linux

**Option 1: One-line Installer**

```bash
curl -s https://api.github.com/repos/JPCodeCraft/AlbionDataAvalonia/releases/latest | jq -r '.assets[] | select(.name == "AFMDataClient_Linux64_Installer.sh") | .browser_download_url' | xargs curl -L -o installer.sh && sed -i 's/\r$//' installer.sh && chmod +x installer.sh && ./installer.sh && rm installer.sh
```

**Option 2: Manual Installation**

1. Download `AFMDataClient_Linux64_Installer.sh` from [releases](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases)
2. Fix line endings: `sed -i 's/\r$//' AFMDataClient_Linux64_Installer.sh`
3. Make executable: `chmod +x AFMDataClient_Linux64_Installer.sh`
4. Run the installer: `./AFMDataClient_Linux64_Installer.sh`

**Uninstallation**

```bash
curl -s https://api.github.com/repos/JPCodeCraft/AlbionDataAvalonia/releases/latest | jq -r '.assets[] | select(.name == "AFMDataClient_Linux64_Uninstaller.sh") | .browser_download_url' | xargs curl -L -o uninstaller.sh && sed -i 's/\r$//' uninstaller.sh && chmod +x uninstaller.sh && ./uninstaller.sh && rm uninstaller.sh
```

> **Note**: Linux version does not support automatic updates. Run the installer again to update.
> **Note**: Linux installation and software had very limited testing. Feel free to contribute to the repository if you can.

### MacOS

AFM Data Client should be able to run on MacOS, but users have reported issues. Feel free to contribute to the repository if you can.
Attention: at the moment there's no MacOS release available to download. To try it you'd need to build it yourself.

## 💻 System Requirements

- Basically, anything that can run Albion Online will run this application

## 📊 Features Showcase

### User Trades

![User Trades](https://github.com/user-attachments/assets/b9287aa0-feb2-43ad-98db-7a0543c4b4f2)

### Settings

![Settings](https://github.com/user-attachments/assets/660ba5ac-3f92-4060-8912-e91eb3a74c97)

## ❓ FAQ

### Is this allowed by the Albion Online developers?

> "Our position is quite simple. As long as you just look and analyze we are ok with it. The moment you modify or manipulate something or somehow interfere with our services we will react (e.g. perma-ban, take legal action, whatever)."
>
> — MadDave, Technical Lead at Sandbox Interactive for Albion Online, 2017 ([source](https://forum.albiononline.com/index.php/Thread/51604-Is-it-allowed-to-scan-your-internet-trafic-and-pick-up-logs/?postID=512670#post512670))

This application:

- ✅ Does NOT modify the game client
- ✅ Does NOT inject code into memory
- ✅ Does NOT track player positions
- ✅ Does NOT display overlays on the game
- 🛜 Does passively capture network packets to collect market data

### How do I make it work with ExitLag?

Change the `Packets redirection method` to `Legacy - NDIS` in your ExitLag app under the `TOOLS` section:

![ExitLag Settings](https://github.com/JPCodeCraft/AlbionDataAvalonia/assets/11092613/94a76ea6-6023-40df-8d6e-e816e612befe)

## 🔗 Related Projects

- [The Albion Online Data Project](https://www.albion-online-data.com/)
- [Albion Free Market](https://albionfreemarket.com/)

## 👨‍💻 Credits

This software was developed by [JP CodeCraft](https://jpcodecraft.com/), the developer behind [Albion Free Market](https://albionfreemarket.com/).

## 📊 Download Statistics

View detailed download statistics [here](https://tooomm.github.io/github-release-stats/?username=jpcodecraft&repository=AlbionDataAvalonia).
