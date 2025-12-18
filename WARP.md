# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Commands you’ll use most

Prereq: .NET SDK 10 (TargetFramework: net10.0).

- Restore and build all projects

  ```bash
  dotnet restore AlbionDataAvalonia.sln
  dotnet build -c Debug AlbionDataAvalonia.sln
  ```

- Run the desktop app (pick your OS host project)

  ```bash
  # Windows
  dotnet run -c Debug --project AlbionDataAvalonia.Desktop
  
  # macOS (arm64 bundle/host project)
  dotnet run -c Debug --project AlbionDataAvalonia.Desktop.MacOS
  
  # Linux
  dotnet run -c Debug --project AlbionDataAvalonia.Desktop.Linux
  ```

- Format/lint C# (uses dotnet-format)

  ```bash
  dotnet format
  ```

- Create tests (repo has no test project yet; follow project guidance in AGENTS.md)

  ```bash
  # create xUnit test project and add to solution
  dotnet new xunit -n AlbionDataAvalonia.Tests
  dotnet sln add AlbionDataAvalonia.Tests/AlbionDataAvalonia.Tests.csproj
  
  # reference projects under test as needed
  dotnet add AlbionDataAvalonia.Tests reference \
    AlbionDataAvalonia/AlbionDataAvalonia.csproj \
    Albion.Network/Albion.Network.csproj \
    PhotonPackageParser/PhotonPackageParser.csproj \
    Protocol16/Protocol16.csproj
  
  # run all tests
  dotnet test -c Debug
  
  # run a single test (by fully-qualified name or trait)
  dotnet test -c Debug --filter FullyQualifiedName~Namespace.Type.Method
  ```

- Publish (self-contained) artifacts

  ```bash
  # Windows (creates publish dir under bin/Release/net10.0-windows/win-x64/publish)
  dotnet publish AlbionDataAvalonia.Desktop/AlbionDataAvalonia.Desktop.csproj -c Release
  
  # Linux (adds installer/uninstaller scripts into publish dir)
  dotnet publish AlbionDataAvalonia.Desktop.Linux/AlbionDataAvalonia.Desktop.Linux.csproj -c Release
  # outputs: AlbionDataAvalonia.Desktop.Linux/bin/Release/net10.0/linux-x64/publish/
  
  # macOS (produces a .app bundle in publish dir)
  dotnet publish AlbionDataAvalonia.Desktop.MacOS/AlbionDataAvalonia.Desktop.MacOS.csproj -c Release
  # outputs: AlbionDataAvalonia.Desktop.MacOS/bin/Release/net10.0/osx-arm64/publish/AFMDataClient_MacOS64.app
  ```

CI reference: see .github/workflows/publish.yml for the multi-OS release pipeline, artifact names, and the LatestVersion.json step.

## High-level architecture and flow

- Solution layout (projects as boundaries)
  - AlbionDataAvalonia (UI + app core): Avalonia 11 app (MVVM via CommunityToolkit.Mvvm), resources in `Views/`, `ViewModels/`, `Themes/`, `Assets/`. References Albion.Network and AlbionDataAvalonia.Shared.
  - AlbionDataAvalonia.Desktop, .Desktop.MacOS, .Desktop.Linux: platform hosts/packaging. Each has its own Program.cs; macOS and Linux projects set RuntimeIdentifier and package app-specific assets on publish.
  - Albion.Network, PhotonPackageParser, Protocol16: packet/protocol libraries (Protocol16 + Photon parser) used to decode Albion Online traffic.
  - AlbionDataAvalonia.Shared: small shared helpers/models used by the libraries.
  - PowBench: console micro-benchmark app for the proof‑of‑work (PoW) solver.

- Composition root and lifetime (App.axaml.cs)
  - EF Core local DB migrations run at startup via `LocalContext`.
  - Services are registered as singletons (see `ServiceCollectionExtensions.AddCommonServices()`):
    - NetworkListenerService (SharpPcap capture + Photon decode), Uploader + AFMUploader (ingestion), SettingsManager, IdleService, MailService, TradeService, LocalizationService, ItemsIdsService, AuthService, CsvExportService, PlayerState.
    - ViewModels: MainViewModel, SettingsViewModel, LogsViewModel, MailsViewModel, TradesViewModel.
  - Serilog configured with a dynamic level switch and rolling file logs under LocalApplicationData/AFMDataClient/logs.
  - Tray icon and native menu are defined in App.axaml; desktop lifetime is explicit shutdown on Windows/Linux and “quit on last window close” on macOS.
  - Windows-only auto-updater runs on a timer, reading `AlbionDataAvalonia.Desktop/LatestVersion.json` and downloading the installer when newer.

- Data capture → decode → upload pipeline
  - NetworkListenerService: opens pcap devices, applies a BPF filter built from known Albion server IPs and the configured UDP port (DefaultAppSettings `PacketFilterPortText`, default `udp port 5056`). It decodes UDP payloads with Photon/Protocol16 and dispatches to request/response handlers.
  - Handlers project market orders, histories, mail, trades, and gold prices into domain models (`MarketUpload`, `MarketHistoriesUpload`, `GoldPriceUpload`) and enqueue them into `Uploader`.
  - Uploader: for each queued unit, optionally sends to AFM first (when `UploadToAfmOnly`), then to the public ingest using a server-provided PoW (see `Network/Pow/`). Upload concurrency is limited by `UserSettings.DesiredThreadCount`.
  - PlayerState aggregates counters and rolling PoW timing stats and exposes them to the UI. MainViewModel binds to these, drives the active view (Dashboard, Logs, Mails, Trades, PCap prerequisites), and owns commands (e.g., open AFM/AODP, install prerequisites).

- Settings and configuration
  - Default files copied to output: `AlbionDataAvalonia/DefaultAppSettings.json`, `DefaultUserSettings.json`.
  - SettingsManager loads user settings from `%LocalAppData%/AFMDataClient/UserSettings.json` (created on first run). App settings are fetched from CDN at runtime; in DEBUG builds the remote fetch is disabled and local defaults are used.
  - Notable keys: ingest subjects (`MarketOrdersIngestSubject`, `MarketHistoriesIngestSubject`, `GoldDataIngestSubject`), updater URLs (`LatestVersionUrl`, `LatesVersionDownloadUrl`, `FileNameFormat`), filter text (`PacketFilterPortText`), AFM auth endpoints, and `ItemsToUploadToAfm` for selective public uploads when AFM‑only mode is on.

- Cross‑platform specifics
  - Packet capture prerequisites: Windows uses Npcap; Linux requires libpcap; macOS requires BPF permissions. The UI exposes an “Install requirements” action; macOS bundles `install/install_access_bpf.sh` and invokes it via `osascript` with admin privileges.
  - Updater logic runs only on Windows. macOS/Linux builds are self‑contained publishes; macOS produces a `.app` bundle; Linux publish includes installer/uninstaller scripts.

## Repository guidance (source of truth)

- See AGENTS.md for concise build/run/publish commands, coding style preferences (nullable enabled, implicit usings), and test conventions. Follow those over any defaults in this file if they diverge.
