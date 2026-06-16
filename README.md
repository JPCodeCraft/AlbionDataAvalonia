# Albion Free Market Data Client

[![Release](https://img.shields.io/github/v/release/JPCodeCraft/AlbionDataAvalonia)](https://github.com/JPCodeCraft/AlbionDataAvalonia/releases)
[![Downloads](https://img.shields.io/github/downloads/JPCodeCraft/AlbionDataAvalonia/total)](https://tooomm.github.io/github-release-stats/?username=jpcodecraft&repository=AlbionDataAvalonia)

## ⚠️ ATTENTION

After installation, the Data Client will be minimized in system tray!

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
| Gathering and Fishing Tracker | Tracks harvested resources, fishing rewards, session value, and silver per hour |
| Market Data Collection | Captures in-game market data and uploads to AODP servers                       |
| Market Mail Tracking   | Collects and displays your market mail data for price tracking                 |
| Trade History          | Stores instant buy/sell trades for later reference                             |
| Damage Tracker         | Tracks combat encounters with damage, DPS, healing, fame, and party/player summaries |
| Enhanced Capture       | Captures data from loadout's quick buy screen, market screens, and gold screen |
| Specs Capture          | Uploads character specs for use with AFM website                               |
| Launch on Startup      | Automatically starts with your system                                          |
| System Tray            | Runs quietly in system tray for minimal interference                           |
| Auto-Updates           | Silent, automatic updates                                                      |
| Configurable Settings  | Adjustable parallelism for AODP's PoW solving and uploads                      |
| User-Friendly          | No admin permissions required (except for NpCap installation)                  |
| Free and Open Source   | This software is free to use and is open source                                |

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

If you are on Apple Silicon, download `AFMDataClient_MacOS_arm64.app.zip`. Intel users should download `AFMDataClient_MacOS_x64.app.zip`.

<img width="1073" height="643" alt="image" src="https://github.com/user-attachments/assets/ce781ade-d2f9-42c8-ba10-bea77bb0ba13" />

The macOS app is not signed, so macOS may say `"AFMDataClient_MacOS" is damaged and can't be opened`. Remove the download quarantine flag before opening it:

<img width="244" height="218" alt="image" src="https://github.com/user-attachments/assets/6db17f1c-d119-4fd6-b855-d89f40fde348" />

1. Make sure `AFMDataClient_MacOS.app` is visible in your `Downloads` folder. Safari may extract the zip automatically. If you still see only the `.zip` file, double-click it first.

<img width="840" height="256" alt="image" src="https://github.com/user-attachments/assets/4c18543a-7da8-4c8c-b4ec-f20a5831e18c" />
   
2. Open Terminal:
   - Press `Command + Space` to open Spotlight.
   - Type `Terminal`.
   - Press `Return`.

<img width="864" height="450" alt="image" src="https://github.com/user-attachments/assets/8e4bcbd0-e3f2-4840-a694-0459d2ab2e86" />

3. In Terminal, go to your Downloads folder:

   ```bash
   cd ~/Downloads
   ```

4. Remove the quarantine flag from the app:

   ```bash
   xattr -dr com.apple.quarantine AFMDataClient_MacOS.app
   ```
<img width="800" height="113" alt="image" src="https://github.com/user-attachments/assets/328bdccf-97a7-4324-974a-68150145ec10" />

5. Double-click `AFMDataClient_MacOS.app` to open it.

   If double-clicking does not open it, run `open AFMDataClient_MacOS.app` in Terminal.

If packet capture is blocked, the app shows a `Capture Blocked` status.

<img width="177" height="166" alt="image" src="https://github.com/user-attachments/assets/3775d64b-e785-4d9c-a5f9-c08cfb41007c" />

It also shows an `Install permissions` button. Click it, approve the macOS administrator prompt, and let the app restart.

<img width="242" height="296" alt="image" src="https://github.com/user-attachments/assets/35751693-36b9-459e-b9a5-fbef9a356767" />

Once it restarts and you get ingame, you should see the app working.

<img width="930" height="631" alt="image" src="https://github.com/user-attachments/assets/84cfa92a-dc84-431a-ae92-c6815d0dea66" />

There's no automatic update feature for MacOS, so you should manually download new versions.

## 💻 System Requirements

- Basically, anything that can run Albion Online will run this application

## 📊 Features Showcase

### Damage Tracker

The Combat tab includes a passive combat tracker for reviewing combat while the client is running. It detects the local player, party members, other players, and mobs from captured packets, then groups health changes and fame gains into combat encounters.

The tracker shows:

- Damage dealt, damage received, healing done, healing received, and fame gained
- Total values, per-second combat rates, and fame-per-hour for the full session or the selected chart window
- Encounter history with duration, status, fame, DPS, damage taken, HPS, and healing received
- Player, party, and mob summaries with filters for all entities, party only, players only, or mobs only
- A time chart with configurable aggregation, total/rate/fame metrics, and 1m, 5m, 10m, 30m, 1h, or unlimited windows

Use the Combat tab's **Pause** button to stop counting health changes without losing tracked entities, or **Reset** to clear the current tracker data. If you do not want combat data tracked, enable **Disable Combat Tracker** in Settings.

### Gathering and Fishing Tracker

The Gathering tab includes a passive tracker for reviewing gathering and fishing sessions. A session starts when the first gathered or fished item is recorded, then tracks harvested resources and confirmed fishing rewards from captured packets.

The tracker shows:

- Total estimated session value, silver per hour, total item amount, and elapsed active time
- Item summaries with item images, amount, estimated market value, total estimated value, and silver per hour
- Minute-by-minute activity with item amount, estimated market value, and silver per hour
- Completed session history with saved session totals and item summaries
- Shareable 2400x1350 PNG cards for completed history sessions
- Pause/resume controls for temporarily ignoring new gathering and fishing events
- Save Session and Discard Session controls for manually closing or clearing the current live session

Active gathering/fishing sessions are checkpointed locally so progress can be recovered if the app closes unexpectedly. On startup, an unfinished checkpoint is closed into History using the last recorded activity time. Completed history is saved locally in SQLite and keeps the estimated values from when the session was recorded.

If no gathered or fished reward is recorded for 30 minutes, the current session is automatically saved to History.

If you do not want gathering and fishing data tracked, enable **Disable Gathering Tracker** in Settings.

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

### How do I back up or migrate my local data (SQLite + settings)?

All local data (SQLite database, settings, cached files) is stored under the app data folder.

**Windows path**

```
C:\Users\<username>\AppData\Local\AFMDataClient
```

**Linux path (default)**

```
~/.local/share/AFMDataClient
```

If `$XDG_DATA_HOME` is set, use:

```
$XDG_DATA_HOME/AFMDataClient
```

**macOS path (default)**

```
~/Library/Application Support/AFMDataClient
```

**Steps**

1. Close AFM (exit from the system tray).
2. Copy the entire `AFMDataClient` folder from the path above.
3. On the new machine, paste it to the same path.
4. Start AFM.

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
