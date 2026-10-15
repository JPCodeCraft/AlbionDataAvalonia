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

<img width="1200" height="929" alt="market" src="https://github.com/user-attachments/assets/5669bcc4-4a7f-4ea6-abf8-f0ae41694b93" />

## 🚀 Features

| Feature                   | Description                                                                    |
| ------------------------- | ------------------------------------------------------------------------------ |
| Market Data Collection | Captures in-game market data and uploads to AODP servers                       |
| Market Mail Tracking   | Collects and displays your market mail data for price tracking                 |
| Trade History          | Stores instant buy/sell trades for later reference                             |
| Activity Tracker       | Saves sessions containing combat encounters, gathering, fishing, loot, silver, observed fame, and participated mob deaths |
| Enhanced Capture       | Captures data from loadout's quick buy screen, market screens, and gold screen |
| Specs Capture          | Uploads character specs for use with AFM website                               |
| Farming Tracker        | Privately records island plots, farming inputs/outputs, focus usage, and observed EMV for your AFM account |
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

### macOS

The macOS version requires macOS 14 or newer. If you are on Apple Silicon, download `AFMDataClient_MacOS_arm64.app.zip`. Intel users should download `AFMDataClient_MacOS_x64.app.zip`.

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

5. Drag `AFMDataClient_MacOS.app` into your `Applications` folder.

6. Open `AFMDataClient_MacOS.app` from `Applications`.

   If double-clicking does not open it, run:

   ```bash
   open /Applications/AFMDataClient_MacOS.app
   ```

If packet capture is blocked, the app shows a `Capture Blocked` status.

<img width="177" height="166" alt="image" src="https://github.com/user-attachments/assets/3775d64b-e785-4d9c-a5f9-c08cfb41007c" />

It also shows an `Install permissions` button. Click it, approve the macOS administrator prompt, and let the app restart.

<img width="242" height="296" alt="image" src="https://github.com/user-attachments/assets/35751693-36b9-459e-b9a5-fbef9a356767" />

Once it restarts and you get ingame, you should see the app working.

<img width="930" height="631" alt="image" src="https://github.com/user-attachments/assets/84cfa92a-dc84-431a-ae92-c6815d0dea66" />

Older AFM installations may instead show `Permissions Update Available` and an `Update permissions` button. Run this one-time update to replace the legacy permission service.

AFM checks for new versions and notifies you, but macOS updates must be downloaded and installed manually.

**Uninstallation**

1. Quit AFM Data Client from the system tray.
2. Open Terminal and remove the packet capture permission service:

   ```bash
   sudo /bin/sh "/Applications/AFMDataClient_MacOS.app/Contents/Resources/uninstall-capture-permissions.sh"
   ```

   If you kept the app somewhere other than `Applications`, adjust the path. The script is safe to run if capture permissions were never installed.

3. Move `AFMDataClient_MacOS.app` from `Applications` to Trash.

The uninstaller preserves your database, backups, settings, and logs. To remove all AFM user data as well, run the following only after confirming that you no longer need those files:

```bash
rm -rf "$HOME/Library/Application Support/AFMDataClient"
```

## 💻 System Requirements

- Windows and Linux: basically anything that can run Albion Online
- macOS: macOS 14 or newer

## 📊 Features Showcase

### Activity Tracker

Activity combines **Current**, **History**, **Items**, and **Cleanup**. Sessions contain encounters and activity between fights; session totals include every assigned event directly. Personal and Party filters use membership saved when each event occurred. Party includes your character. Items also supports All observed and source filters for loot, gathering, and fishing. **Total Silver** and **Total Silver / Hour** combine cash pickups with estimated loot, gathering, and fishing value for the selected period and scope; their tooltip explains estimates and missing values. **Loot value** includes loot only; gathered items, their EMV, and Gathering Fame share the gathering panel.

Current includes a rolling **5, 10, or 15 minute** timeline with damage/healing areas, grouped outcome and reward markers, and encounter combat spans. Nearby rewards are grouped more strongly for longer windows. **Dealt / Received** changes the health series. Hover holds the display while recording continues; moving the mouse out resumes it automatically. Markers show compact tooltips, and encounter spans open history. History charts use the same display over the whole session or selected encounter, with at most 600 rate bins. The graph includes idle zeros; headline DPS/HPS uses combat time. Capture gaps are shown separately from downtime. **Gathering** chart mode shows harvested and fished items per second on a separate scale. **Show** filters apply to charts and events, leaving whole-period cards unchanged; selected categories match any. History **Contains all** tags match every selected tag. Loot and silver in PvE/PvP views remain earnings observed during matching encounters; fame uses its independently matched source.

Relevant damage or attributable healing opens an encounter and refreshes its seven-second inactivity deadline. Loot, silver, fame and outcome signals never extend combat. Encounter DPS/HPS uses the first-to-last combat span with a one-second minimum. Encounter earnings mean **observed during the encounter**, without claiming the fight produced them.

Successful instance entry becomes a session only when relevant activity is observed. Supported dungeon floors can continue one run. Known-instance reentry within two minutes can resume a run when no intervening relevant activity or local lethal death prevents it. Roaming survives ordinary zone changes and ends on hub return, instance entry, Finish, character/server change or 30 minutes without relevant activity. Session DPS/HPS uses the combined combat spans of encounters with activity in the selected scope, excluding time between fights and the closing delay. Earnings per hour use observed session time, including travel and downtime but excluding explicit capture gaps and dungeon-exit intervals.

Mob counts require prior personal/party damage in the same lifetime. Explicit zero health confirms terminal evidence; omitted resulting health is labelled inferred. Visibility departure does not imply death. A dead player who acts or is healed again counts as respawned or revived. Cash silver stays separate from estimated loot value. Only canonical silver pickups contribute cash; overlapping broadcasts remain supplemental. Omitted tax fields count as no tax for you and your party members alike. Reported healing is preserved without claiming effective healing.

**Combat Fame** and **Gathering Fame** are separate; Gathering Fame includes fishing, with a fishing breakdown in its tooltip. Fame awards match preceding Destiny Board progression on the same actor and reliable Photon stream, within four messages and 250 ms. The achievement reference's `killmobfame`, `gatherfame`, and `fishingfame` mission types identify the source independently of the active encounter. Missing or conflicting source evidence remains Unknown. Progress updates never create additional awards. The captured positive bonus is applied once to the award; cumulative player fame is retained only as detail. Existing awards with saved, valid components are corrected when their old calculation can be reproduced, but historical source categories are not guessed. PvP fame packet meanings remain unverified; player kills and encounter types cannot supply a fame amount.

Gathering and fishing packets can also report an additional progression component. When components 12/13 are valid, positive, equal, and the source is matched, one component is added to the ordinary award. This component already includes its applicable bonuses; applying premium or the reported bonus to it again would duplicate them. Captures with and without a reported bonus verify the resulting progression increment. Saved rows without these components cannot safely be reconstructed from ordinary fame.

Inventory requests create short-lived candidates. Your pickup is recorded when the server confirms the exact transfer into your inventory, or into a bag or equipment slot named by your own request; when a unique source decrement matches a gain in your inventory; or when the requested item leaves its loot container, which also covers stacks merged into existing inventory stacks. Banks, rearrangements and failed requests do not contribute personal/party earnings. Party chests attribute pickups to the player named by the loot allocation, and type-only removals to the single party member allowed to open that chest, unless your own pending request or an already recorded pickup explains the removal. A removal already explained by a pickup broadcast is not recorded again. Correlations follow capture time, and pickups still inside their correlation window are resolved before a map change, **Finish** or disabling recording. Pickups seen before the character and map are confirmed are kept outside sessions; party members among them are recognised from the party roster, which is kept across the first map join. Removals without a known recipient, and items missing from a public chest when it is reopened or emptied, are kept as inferred pickups by Unknown; they are shown and exported but not included in totals. Item names come from reference data whenever a pickup omits them. Loot is valued with the Black Market estimate when available, then the normal estimate; pickups without quality use the average of qualities 1-4, labelled in their tooltip. Gathering and fishing keep normal-quality estimates. Known estimates are frozen; missing prices may be filled later through the existing authenticated, batched EMV lookup and cache. Opening item history also requests missing estimates. Looter and looted-player guild and alliance come from the player directory, including players seen on earlier maps, and are completed for recent pickups when a player's identity arrives. Loot lists show them as Guild/Alliance (looter) and Source guild/Source alliance (looted player) columns, and loot search matches both. **Export loot…** on Current, a History session or Items exports exactly the loot list shown there, across all pages. History remains until explicitly deleted. Disable **Activity recording** to close current recording while keeping history readable; **Finish** permanently ends that session.

**Cleanup** shows Activity storage, the whole local database size, and session, encounter, event, and pickup counts. Preview removal of old history or loot picked up outside your party, then confirm deletion. Open sessions and original gathering records are kept. Deletion updates surviving totals and prevents pending amendments from restoring removed events. Freed SQLite space is reused; the file need not shrink immediately.

The journal batches writes every second or 250 changes. Database failures retain accepted work and visibly suspend recording if needed; the session records a gap, keeps the current map, character and inventory context, and resumes recording when writes are accepted again. A write the database rejects as invalid is logged and dropped instead of blocking later writes. A capture restart within a minute (adapter change or traffic recovery) records a gap and continues on the same map and character; after a longer stop, recording waits for fresh identity/map confirmation. When the server IP is not recognised (VPN or routing tools), activity is kept under the last known server, or server 0. Traffic silence is not logout. Process restart marks unfinished activity Interrupted. Shutdown drains recording before disposing capture/core services. Existing SQLite backups include Activity tables.

Debug builds log raw Activity packet probes when **Logs → Log verbosity** is **Debug** or **Verbose**. Probes cover every Activity subscription and inventory intent, plus resource identities, harvest/fishing lifecycle, and achievement/journal progression for fame-source investigation. Event, request, and response logs retain all supplied parameters, capture time, and transport identity; responses also include the result code. Information or higher stops packet probes.

The Activity schema is provided by the EF-generated `20261007145317_AddActivityJournal` migration. Existing startup database initialization applies pending migrations when the application is launched. Do not hand-edit migration files; generate future schema changes with EF Core commands.

### Gathering and fishing in Activity

Confirmed harvests from your character or party and confirmed local fishing rewards are recorded by Activity. They start or refresh the session inactivity deadline, but never start or prolong an encounter. Sessions receive Gathering, PvE, and PvP tags from the events visible in the selected scope; a session can have several tags. Fishing is part of Gathering and can also be filtered separately. Fame remains a packet award, and cash silver stays separate from all item estimates.

Fishing requires a successful completion response, a server reward, and a unique matching gain in your inventory. A completed catch is kept when you cancel, move items or leave the map right after it, catches that merge into older inventory stacks are recognised, and catches are settled on capture time so delayed processing cannot drop them. Shared inventory claims prevent the same acquisition from counting as both loot and fishing. Requests and ambiguous rewards are not personal earnings.

Existing gathering history is converted automatically at startup into closed Activity sessions with item summary records. The original gathering tables and rows remain untouched. Conversion preserves stored quantities, values, dates, and observed duration; it does not invent individual pickup times, map locations, fame, cash, or combat. Imported runs are labelled as summaries and have no temporal chart or encounters. Valid orphan checkpoints become partial Interrupted sessions; completed records take precedence. Conversion uses deterministic identifiers and durable import markers, so rerunning it does not duplicate runs or restore deleted Activity history. Invalid records and inconsistent totals are reported while the source data remains intact. This conversion uses the existing Activity schema and needs no additional migration.

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

### How do I restore an automatic SQLite backup?

AFM creates a compressed database backup every 24 hours. It keeps the three newest backups plus recovery points closest to 5, 10, and 15 days old. You can open the backup folder and see the same restore instructions in **Settings > Backup**.

Automatic backups are stored under the app data folder in `backups`. Each ZIP contains `afmdataclient.db`. Stored history and player data are included, but the saved sign-in token is removed.

To restore a backup:

1. Exit AFM from the system tray and confirm that it is no longer running.
2. Copy the current `data` folder somewhere safe.
3. Extract the selected backup ZIP.
4. In the `data` folder, move aside `afmdataclient.db` and any `afmdataclient.db-wal` or `afmdataclient.db-shm` files.
5. Copy the extracted `afmdataclient.db` into the `data` folder.
6. Start AFM, verify the restored history, and sign in again.

### How do I migrate all local data, including settings?

All local data (SQLite database, settings, cached files, and automatic backups) is stored under the app data folder.

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


## Building with the shared core

The desktop and Android clients share [AFMDataClientCore](https://github.com/JPCodeCraft/AFMDataClientCore). This repository pins its library at `shared/AFMDataClientCore`; no sibling checkout or NuGet feed is required to build.

```bash
git clone --recurse-submodules https://github.com/JPCodeCraft/AlbionDataAvalonia.git
cd AlbionDataAvalonia
git submodule update --init --recursive
dotnet restore AlbionDataAvalonia.sln
dotnet build -c Debug AlbionDataAvalonia.sln
dotnet test PowBench.Tests
```

After switching branches or pulling a change to the submodule pointer, run `git submodule update --init --recursive` again. Shared changes are committed and pushed in AFMDataClientCore first; each client then updates its pinned commit independently. A standalone development checkout can live beside the clients at `Repos/AFMDataClientCore`, while builds always reference the pinned submodule.

`DesktopClientCore` explicitly enables market orders (including loadouts), market history, specs, EMV, islands, gold, and world events. It adapts desktop settings, account credentials, and upload statistics to the shared runtime. Activity, party, awakened items, market mail, and trade history remain desktop services and subscribe after shared packet processing. Capture drivers, database schemas, installers, and UI stay in the desktop repository.

The app embeds gzipped base copies of its reference data (items, locations, achievements, mobs and awakened-item data) from `AlbionDataAvalonia/ReferenceData`. They are used only when a file cannot be downloaded and has no validated disk cache yet, such as a first start without access to the CDN. Run `AlbionDataAvalonia/ReferenceData/update.sh` before a release to refresh them from the CDN.

Shared-library integration validation includes building the desktop solution and running the PoW correctness tests. Manual checks cover ordinary and loadout market routing, gold and bandit uploads during Private Mode, authenticated specs/EMV/world events, local tracker updates, account/server changes, and island collection, failed actions, offline retry, and restart recovery. Cross-platform capture and UI checks require the corresponding operating systems; builds alone do not validate live game traffic.

## Private Mode and sharing

Private Mode saves all captured buy/sell orders, including loadout offers, to your
AFM account for the flipper and website price tools. No market-order categories
are also sent to AODP. Market history, gold prices and bandit events remain public.
Private Mode and both sharing choices default off and are saved in UserSettings.json.
All three survive restarts, sign-out and account changes. Turning Private Mode off
keeps the sharing choices saved for when it is enabled again. Sharing only applies
to private orders while signed in; private order uploads are skipped while signed
out, without a public fallback or an offline queue.

Settings > Sharing accepts a user ID, email or exact AFM website username in one
field. The website username is not an Albion character name. Both users must add
each other; up to five friends are supported. Ambiguous matches are rejected; use
email instead. Existing entries remain tied to the saved account if a username
changes. Private and shared price summaries expire up to 48 hours after their
five-minute observation bin starts. Turning sharing off affects new uploads only.

Release validation: enable Private Mode and both sharing choices, restart, sign
out and sign back in (also with another account); verify all choices stay selected.
Turn Private Mode off and back on, then disable each sharing choice and repeat
restart/sign-out checks to confirm both enabled and disabled values persist. Capture an
ordinary item and each former public exception (runes, souls, relics, alchemy
extracts and fish sauce), including loadout offers, and confirm no market orders
reach AODP. Verify history/gold/bandit uploads remain public. Check tooltips and the
Sharing field on a narrow window, and add/remove friends by all three identifiers.
These UI/capture checks require a signed-in client and live game traffic.
