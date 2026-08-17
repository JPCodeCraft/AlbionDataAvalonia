using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Handlers;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Party;
using AlbionDataAvalonia.Players;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.Win32;
using PacketDotNet;
using PhotonPackageParser;
using Serilog;
using SharpPcap;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class NetworkListenerService : IDisposable
    {
        private const string MacOSCapturePermissionSetupScriptName = "setup-capture-permissions.sh";
        private const string MacOSCapturePermissionLaunchDaemonPath =
            "/Library/LaunchDaemons/com.albionfreemarket.afmdataclient.chmodbpf.plist";
        private const string MacOSLegacyCapturePermissionScheduleKey = "<key>StartInterval</key>";
        private readonly HashSet<string> _unknownServerIps = new HashSet<string>();
        private readonly object _lifecycleStateLock = new object();
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);

        private readonly Uploader _uploader;
        private readonly AFMUploader _afmUploader;
        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;
        private readonly MailService _mailService;
        private readonly TradeService _tradeService;
        private readonly IdleService _idleService;
        private readonly ItemsIdsService _itemsIdsService;
        private readonly ItemEstimatedMarketValueService _itemEstimatedMarketValues;
        private readonly AchievementsService _achievementsService;
        private readonly CombatTrackerService _combatTracker;
        private readonly GatheringTrackerService _gatheringTracker;
        private readonly PartyTrackerService _partyTracker;
        private readonly PlayerIdentityService _playerIdentityService;
        private readonly LootTrackerService _lootTracker;
        private readonly MobsService _mobsService;
        private readonly LegendaryItemTrackerService _legendaryTracker;

        private CancellationTokenSource? _lifecycleCancellation;
        private CaptureSession? _activeSession;
        private long _lifecycleGeneration;
        private bool _listeningRequested;
        private bool _isPowerSuspended;
        private bool _disposed;

        public event EventHandler? MacOSCapturePermissionSetupRequiredChanged;
        public bool IsMacOSCapturePermissionSetupRequired { get; private set; }
        public bool IsMacOSCapturePermissionSetupOutdated { get; private set; }

        public NetworkListenerService(Uploader uploader, PlayerState playerState, SettingsManager settingsManager, MailService mailService, IdleService idleService, TradeService tradeService, AFMUploader afmUploader, ItemsIdsService itemsIdsService, ItemEstimatedMarketValueService itemEstimatedMarketValues, AchievementsService achievementsService, CombatTrackerService combatTracker, GatheringTrackerService gatheringTracker, PartyTrackerService partyTracker, PlayerIdentityService playerIdentityService, LootTrackerService lootTracker, MobsService mobsService, LegendaryItemTrackerService legendaryTracker)
        {
            _uploader = uploader;
            _playerState = playerState;
            _settingsManager = settingsManager;
            _mailService = mailService;
            _idleService = idleService;
            _itemsIdsService = itemsIdsService;
            _itemEstimatedMarketValues = itemEstimatedMarketValues;
            _achievementsService = achievementsService;
            _combatTracker = combatTracker;
            _gatheringTracker = gatheringTracker;
            _partyTracker = partyTracker;
            _playerIdentityService = playerIdentityService;
            _lootTracker = lootTracker;
            _mobsService = mobsService;
            _legendaryTracker = legendaryTracker;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            }

            _idleService.OnDetectedIdle += RestartNetworkListener;
            _tradeService = tradeService;
            _afmUploader = afmUploader;
            IsMacOSCapturePermissionSetupOutdated =
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                && HasLegacyMacOSCapturePermissionSetup();
        }

        public Task StartNetworkListeningAsync()
        {
            return RequestStartAsync(forceRestart: false);
        }

        private Task RequestStartAsync(bool forceRestart)
        {
            LifecycleRequest request;
            CancellationTokenSource? previousCancellation;

            lock (_lifecycleStateLock)
            {
                if (_disposed)
                {
                    Log.Debug("Ignoring network listener start because the service is disposed.");
                    return Task.CompletedTask;
                }

                if (_isPowerSuspended)
                {
                    Log.Debug("Ignoring network listener start while the system is suspended.");
                    return Task.CompletedTask;
                }

                if (_listeningRequested && !forceRestart)
                {
                    Log.Information("Network listening is already active or starting.");
                    return Task.CompletedTask;
                }

                _listeningRequested = true;
                request = CreateLifecycleRequestLocked();
                previousCancellation = ReplaceLifecycleCancellationLocked(request.Cancellation);
            }

            CancelLifecycleRequest(previousCancellation);
            return ApplyStartRequestAsync(request, forceRestart);
        }

        private async Task ApplyStartRequestAsync(LifecycleRequest request, bool forceRestart)
        {
            CaptureSession? pendingSession = null;
            var acquiredGate = false;
            var startSucceeded = false;

            try
            {
                await _lifecycleGate.WaitAsync(request.Token).ConfigureAwait(false);
                acquiredGate = true;

                if (!IsCurrentRequest(request, listeningRequested: true))
                {
                    return;
                }

                if (forceRestart)
                {
                    var previousSession = Interlocked.Exchange(ref _activeSession, null);
                    TerminateSession(previousSession);
                }
                else if (Volatile.Read(ref _activeSession) is not null)
                {
                    startSucceeded = true;
                    return;
                }

                //AWAIT SOME SECONDS FOR NETWORK STUFF TO BE READY
                Log.Information($"Waiting {_settingsManager.AppSettings.NetworkDevicesStartDelaySecs} seconds for network drivers to be ready");
                await Task.Delay(
                        TimeSpan.FromSeconds(_settingsManager.AppSettings.NetworkDevicesStartDelaySecs),
                        request.Token)
                    .ConfigureAwait(false);

                request.Token.ThrowIfCancellationRequested();
                if (!IsCurrentRequest(request, listeningRequested: true))
                {
                    return;
                }

                if (NpCapInstallationChecker.IsNpCapInstalled() == false)
                {
                    Log.Error("NpCap is not installed, please install it to use this application");
                    return;
                }

                var filter = _settingsManager.AppSettings.PacketFilterPortText ?? string.Empty;

                ReceiverBuilder builder = ReceiverBuilder.Create();

                // ADD HANDLERS HERE
                // EVENTS
                // builder.AddEventHandler(new LeaveEventHandler(_playerState));
                // builder.AddEventHandler(new PlayerCountsEventHandler(_playerState, _afmUploader));
                // builder.AddEventHandler(new CharacterStatsEventHandler());
                builder.AddEventHandler(new NewCharacterEventHandler(
                    _combatTracker,
                    _partyTracker,
                    _playerIdentityService,
                    _playerState));
                builder.AddEventHandler(new NewMobEventHandler(_combatTracker, _mobsService));
                builder.AddEventHandler(new PartyJoinedEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyPlayerJoinedEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyPlayerLeftEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyDisbandedEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyOnClusterPartyJoinedEventHandler(_partyTracker));
                builder.AddEventHandler(new PartySetRoleFlagEventHandler(_partyTracker));
                builder.AddEventHandler(new HealthUpdateEventHandler(_combatTracker));
                builder.AddEventHandler(new HealthUpdatesEventHandler(_combatTracker));
                builder.AddEventHandler(new UpdateFameEventHandler(_combatTracker));
                builder.AddEventHandler(new TakeSilverEventHandler(_combatTracker));
                builder.AddEventHandler(new InCombatStateUpdateEventHandler(_combatTracker));
                builder.AddEventHandler(new TimeSyncEventHandler(_combatTracker));
                builder.AddEventHandler(new EstimatedMarketValueUpdateEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _playerState));
                builder.AddEventHandler(new FullAchievementInfoEventHandler(_achievementsService, _playerState, _afmUploader, _settingsManager));
                builder.AddEventHandler(new FestivitiesUpdateEventHandler(_playerState, _afmUploader));
                builder.AddEventHandler(new RedZoneWorldMapEventHandler(_playerState, _uploader));
                builder.AddEventHandler(new HarvestFinishedEventHandler(_gatheringTracker));
                builder.AddEventHandler(new RewardGrantedEventHandler(_gatheringTracker));
                builder.AddEventHandler(new NewLootEventHandler(_lootTracker));
                builder.AddEventHandler(new NewLootChestEventHandler(_lootTracker));
                builder.AddEventHandler(new UpdateLootChestEventHandler(_lootTracker));
                builder.AddEventHandler(new LootChestOpenedEventHandler(_lootTracker));
                builder.AddEventHandler(new AttachItemContainerEventHandler(_lootTracker, _legendaryTracker));
                builder.AddEventHandler(new DetachItemContainerEventHandler(_lootTracker, _legendaryTracker));
                builder.AddEventHandler(new InventoryDeleteItemEventHandler(_lootTracker, _legendaryTracker));
                builder.AddEventHandler(new InventoryPutItemEventHandler(_legendaryTracker));
                builder.AddEventHandler(new OtherGrabbedLootEventHandler(_lootTracker));
                builder.AddEventHandler(new PartyLootItemsEventHandler(_lootTracker));
                builder.AddEventHandler(new PartyLootItemsRemovedEventHandler(_lootTracker));
                builder.AddEventHandler(new PartyLootItemTypesRemovedEventHandler(_lootTracker));
                builder.AddEventHandler(new NewSimpleItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _gatheringTracker, _lootTracker, _playerState));
                builder.AddEventHandler(new NewJournalItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _lootTracker, _playerState));
                builder.AddEventHandler(new NewLaborerItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _lootTracker, _playerState));
                builder.AddEventHandler(new NewEquipmentItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _lootTracker, _playerState, _legendaryTracker));
                builder.AddEventHandler(new NewFurnitureItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _lootTracker, _playerState));
                builder.AddEventHandler(new NewKillTrophyItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _lootTracker, _playerState));
                builder.AddEventHandler(new NewSiegeBannerItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _lootTracker, _playerState));
                builder.AddEventHandler(new NewEquipmentItemLegendarySoulEventHandler(_legendaryTracker));
                builder.AddEventHandler(new BankVaultInfoEventHandler(_legendaryTracker));
                builder.AddEventHandler(new GuildVaultInfoEventHandler(_legendaryTracker));
#if DEBUG
                builder.AddEventHandler(new DebugEventProbeEventHandler());
#endif
                // RESPONSE
                builder.AddResponseHandler(new AuctionGetLoadoutOffersResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new AuctionGetOffersResponseHandler(_uploader, _playerState, _tradeService));
                builder.AddResponseHandler(new AuctionGetRequestsResponseHandler(_uploader, _playerState, _tradeService));
                builder.AddResponseHandler(new AuctionGetItemAverageStatsResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new JoinResponseHandler(_playerState, _afmUploader, _partyTracker, _lootTracker, _legendaryTracker));
                builder.AddResponseHandler(new AuctionGetGoldAverageStatsResponseHandler(_uploader));
                builder.AddResponseHandler(new GetMailInfosResponseHandler(_playerState, _mailService));
                builder.AddResponseHandler(new ReadMailResponseHandler(_playerState, _mailService));
                builder.AddResponseHandler(new AuctionBuyOfferResponseHandler(_playerState, _tradeService));
                builder.AddResponseHandler(new AuctionSellSpecificItemRequestResponseHandler(_playerState, _tradeService));
                builder.AddResponseHandler(new FishingFinishResponseHandler(_gatheringTracker));
#if DEBUG
                builder.AddHandler(new DebugResponseProbeResponseHandler());
#endif
                // builder.AddResponseHandler(new AssetOverviewResponseHandler(_playerState));
                // builder.AddResponseHandler(new AssetOverviewUnfreezeCacheResponseHandler(_playerState));
                // builder.AddResponseHandler(new AssetOverviewTabsResponseHandler(_playerState));
                // builder.AddResponseHandler(new AssetOverviewTabContentResponseHandler(_playerState));
                // REQUEST
                builder.AddRequestHandler(new AuctionGetItemAverageStatsRequestHandler(_playerState));
                builder.AddRequestHandler(new AuctionBuyOfferRequestHandler(_playerState, _tradeService));
                builder.AddRequestHandler(new AuctionSellSpecificItemRequestRequestHandler(_playerState, _tradeService));
                builder.AddRequestHandler(new FishingStartRequestHandler(_gatheringTracker));
                builder.AddRequestHandler(new FishingFinishRequestHandler(_gatheringTracker));
                builder.AddRequestHandler(new FishingCancelRequestHandler(_gatheringTracker));
                builder.AddRequestHandler(new InventoryMoveItemRequestHandler(_lootTracker));
                builder.AddRequestHandler(new InventoryMoveGivenItemsRequestHandler(_lootTracker));
#if DEBUG
                builder.AddHandler(new DebugRequestProbeRequestHandler());
#endif

                var localReceiver = builder.Build();

                if (localReceiver == null)
                {
                    Log.Error("Failed to create network receiver");
                    return;
                }

                Log.Debug("Starting network device listening");

                var discoveredDevices = CaptureDeviceList.New();
                if (!discoveredDevices.Any())
                {
                    Log.Error("No network capture devices were found.");
                    return;
                }

                pendingSession = new CaptureSession(localReceiver, this);
                var openedDeviceCount = 0;
                var failedDeviceCount = 0;
                var sawPermissionDenied = false;
                foreach (var device in discoveredDevices)
                {
                    request.Token.ThrowIfCancellationRequested();

                    var registration = new CaptureDeviceRegistration(
                        device,
                        pendingSession.PacketHandler);
                    var result = await Task.Run(() => TryStartDeviceCapture(registration, filter))
                        .ConfigureAwait(false);
                    if (result.Opened)
                    {
                        pendingSession.Devices.Add(registration);
                        openedDeviceCount++;
                    }
                    else
                    {
                        failedDeviceCount++;
                        sawPermissionDenied |= result.PermissionDenied;
                    }
                }

                if (openedDeviceCount == 0)
                {
                    LogNoCaptureDevicesOpened(failedDeviceCount, sawPermissionDenied);
                    return;
                }

                if (failedDeviceCount > 0)
                {
                    Log.Warning(
                        "Opened {OpenedDeviceCount} network capture device(s), but failed to open {FailedDeviceCount}.",
                        openedDeviceCount,
                        failedDeviceCount);
                }

                request.Token.ThrowIfCancellationRequested();
                SetMacOSCapturePermissionSetupRequired(false);
                if (!TryPublishSession(request, pendingSession, out var replacedSession))
                {
                    return;
                }

                pendingSession = null;
                TerminateSession(replacedSession);

                Log.Information("Listening to Albion network packages!");
                startSucceeded = true;
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
            {
                Log.Debug("Network listener start was superseded by a newer lifecycle request.");
            }
            catch (Exception ex)
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && IsPacketCapturePermissionError(ex))
                {
                    SetMacOSCapturePermissionSetupRequired(true);
                    Log.Error(ex, "Error starting network listening because macOS denied packet capture access.");
                    LogMacOSCapturePermissionHelp();
                }
                else
                {
                    SetMacOSCapturePermissionSetupRequired(false);
                    Log.Error(ex, "Error starting network listening");
                }
            }
            finally
            {
                TerminateSession(pendingSession);

                if (acquiredGate)
                {
                    _lifecycleGate.Release();
                }

                CompleteLifecycleRequest(request, failedStart: !startSucceeded);
            }
        }

        private CaptureDeviceOpenResult TryStartDeviceCapture(
            CaptureDeviceRegistration registration,
            string filter)
        {
            var device = registration.Device;

            try
            {
                Log.Debug("Opening network device: {Device}", registration.DisplayName);

                device.OnPacketArrival += registration.PacketHandler;
                device.Open(new DeviceConfiguration
                {
                    Mode = DeviceModes.None,
                    ReadTimeout = 5000
                });
                device.Filter = filter;
                device.StartCapture();

                Log.Debug(
                    "Opened network device: {Device} with filter: {Filter}",
                    registration.DisplayName,
                    filter);
                return new CaptureDeviceOpenResult(true, false);
            }
            catch (Exception ex)
            {
                TerminateDeviceCapture(registration);
                Log.Warning(
                    ex,
                    "Error initializing network device {Device}.",
                    registration.DisplayName);
                return new CaptureDeviceOpenResult(false, IsPacketCapturePermissionError(ex));
            }
        }

        public async Task<bool> InstallMacOSCapturePermissionsAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Log.Warning("macOS packet capture permission setup is only available on macOS.");
                return false;
            }

            var setupScriptPath = GetMacOSCapturePermissionSetupScriptPath();
            if (!File.Exists(setupScriptPath))
            {
                Log.Error("macOS packet capture permission setup script was not found at {SetupScriptPath}.", setupScriptPath);
                return false;
            }

            var shellCommand = "/bin/sh " + QuoteForPosixShell(setupScriptPath);
            var appleScript = string.Format(
                "do shell script \"{0}\" with administrator privileges",
                EscapeForAppleScript(shellCommand));

            var startInfo = new ProcessStartInfo
            {
                FileName = "osascript",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add(appleScript);

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    Log.Error("Unable to start macOS packet capture permission setup.");
                    return false;
                }

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                var output = await outputTask;
                var error = await errorTask;

                if (process.ExitCode == 0)
                {
                    IsMacOSCapturePermissionSetupOutdated = false;
                    Log.Information("macOS packet capture permission setup completed. Restart AFM Data Client. If capture is still denied, log out and back in or reboot.");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Log.Debug("macOS packet capture permission setup output: {Output}", output.Trim());
                    }

                    return true;
                }

                Log.Warning(
                    "macOS packet capture permission setup did not complete. Exit code: {ExitCode}. Output: {Output}. Error: {Error}",
                    process.ExitCode,
                    output.Trim(),
                    error.Trim());
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error running macOS packet capture permission setup.");
                return false;
            }
        }

        private void LogNoCaptureDevicesOpened(int failedDeviceCount, bool permissionDenied)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && permissionDenied)
            {
                SetMacOSCapturePermissionSetupRequired(true);
                Log.Error(
                    "macOS denied packet capture access for all {FailedDeviceCount} network device(s).",
                    failedDeviceCount);
                LogMacOSCapturePermissionHelp();
                return;
            }

            SetMacOSCapturePermissionSetupRequired(false);
            Log.Error("No network capture devices could be opened. Failed devices: {FailedDeviceCount}.", failedDeviceCount);
        }

        private void SetMacOSCapturePermissionSetupRequired(bool isRequired)
        {
            var nextValue = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && isRequired;
            if (IsMacOSCapturePermissionSetupRequired == nextValue)
            {
                return;
            }

            IsMacOSCapturePermissionSetupRequired = nextValue;
            MacOSCapturePermissionSetupRequiredChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void LogMacOSCapturePermissionHelp()
        {
            Log.Error(
                "Run the macOS packet capture permission setup once, then restart AFM Data Client: sudo /bin/sh \"{SetupScriptPath}\". If capture is still denied after setup, log out and back in or reboot.",
                GetMacOSCapturePermissionSetupScriptPath());
        }

        private static string GetMacOSCapturePermissionSetupScriptPath()
        {
            var bundleScriptPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "Resources",
                MacOSCapturePermissionSetupScriptName));

            return File.Exists(bundleScriptPath)
                ? bundleScriptPath
                : Path.Combine(
                    "AFMDataClient_MacOS.app",
                    "Contents",
                    "Resources",
                    MacOSCapturePermissionSetupScriptName);
        }

        private static bool HasLegacyMacOSCapturePermissionSetup()
        {
            if (!File.Exists(MacOSCapturePermissionLaunchDaemonPath))
            {
                return false;
            }

            try
            {
                return File.ReadAllText(MacOSCapturePermissionLaunchDaemonPath)
                    .Contains(MacOSLegacyCapturePermissionScheduleKey, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                Log.Warning(
                    ex,
                    "Unable to inspect the installed macOS packet capture permission service at {PlistPath}.",
                    MacOSCapturePermissionLaunchDaemonPath);
                return false;
            }
        }

        private static string QuoteForPosixShell(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }

        private static string EscapeForAppleScript(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static bool IsPacketCapturePermissionError(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                var message = current.Message;
                if (message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("denied", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("/dev/bpf", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("BIOC", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetDeviceDisplayName(ILiveDevice device)
        {
            return string.IsNullOrWhiteSpace(device.Description)
                ? device.Name
                : device.Description;
        }

        private void PacketHandler(CaptureSession session, object? sender, PacketCapture e)
        {
            if (!session.IsReady || !ReferenceEquals(Volatile.Read(ref _activeSession), session))
            {
                return;
            }

            lock (session.ProcessingLock)
            {
                if (!session.IsReady || !ReferenceEquals(Volatile.Read(ref _activeSession), session))
                {
                    return;
                }

                ProcessPacket(session, e);
            }
        }

        private void ProcessPacket(CaptureSession session, PacketCapture e)
        {
            try
            {
                UdpPacket packet = Packet.ParsePacket(e.GetPacket().LinkLayerType, e.GetPacket().Data).Extract<UdpPacket>();
                if (packet != null)
                {
                    var selectedDevice = Interlocked.CompareExchange(
                        ref session.SelectedDevice,
                        e.Device,
                        null);
                    if (selectedDevice is not null && !ReferenceEquals(selectedDevice, e.Device))
                    {
                        return;
                    }

                    if (selectedDevice is null)
                    {
                        foreach (var registration in session.Devices)
                        {
                            if (!ReferenceEquals(registration.Device, e.Device))
                            {
                                _ = Task.Run(() => TerminateDeviceCapture(registration));
                            }
                        }
                    }

                    _playerState.LastPacketTime = DateTime.UtcNow;

                    var srcIp = (packet.ParentPacket as IPPacket)?.SourceAddress?.ToString();

                    if (string.IsNullOrEmpty(srcIp))
                    {
                        Log.Verbose("Packet Source IP null or empty, ignoring");
                        return;
                    }
                    var server = AlbionServers.GetAll().SingleOrDefault(x => x.HostIps.Any(prefix => srcIp.StartsWith(prefix)));
                    if (server is not null)
                    {
                        //Log.Verbose("Packet from {server} server from IP {ip}", server.Name, srcIp);
                        _playerState.AlbionServer = server;
                    }
                    else if (!IsPrivateIp(srcIp) && _unknownServerIps.Add(srcIp))
                    {
                        Log.Warning("Received packet from unknown IP {Ip} — could not determine Albion server. Known unknown IPs so far: {Ips}", srcIp, string.Join(", ", _unknownServerIps));
                    }

                    if (!ReferenceEquals(Volatile.Read(ref _activeSession), session))
                    {
                        return;
                    }

                    var packetStatus = session.Receiver.ReceivePacket(packet.PayloadData);
                    if (packetStatus == PacketStatus.Encrypted)
                    {
                        _playerState.HasEncryptedData = true;
                        Log.Warning("Encrypted packet received! You can't see market orders!");
                    }
#if DEBUG
                    else if (packetStatus == PacketStatus.InvalidHeader ||
                        packetStatus == PacketStatus.InvalidCrc)
                    {
                        Log.Debug(
                            "Photon UDP payload rejected. Status={PacketStatus}, PayloadBytes={PayloadBytes}",
                            packetStatus,
                            packet.PayloadData.Length);
                    }
#endif
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while processing captured Albion packet");
            }
        }

        private static bool IsPrivateIp(string ip)
        {
            return ip.StartsWith("10.") ||
                   ip.StartsWith("127.") ||
                   ip.StartsWith("169.254.") ||
                   ip.StartsWith("192.168.") ||
                   ip == "::1" ||
                   (ip.StartsWith("172.") && System.Net.IPAddress.TryParse(ip, out var addr) &&
                    addr.GetAddressBytes() is var b && b.Length == 4 && b[1] >= 16 && b[1] <= 31);
        }

        private void TerminateDeviceCapture(CaptureDeviceRegistration registration)
        {
            if (Interlocked.Exchange(ref registration.IsTerminated, 1) != 0)
            {
                registration.TerminationCompleted.Task.GetAwaiter().GetResult();
                return;
            }

            var device = registration.Device;

            try
            {
                Log.Debug("Closing network device: {Device}", registration.DisplayName);

                try
                {
                    device.OnPacketArrival -= registration.PacketHandler;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Error unsubscribing from network device {Device}.",
                        registration.DisplayName);
                }

                try
                {
                    device.StopCapture();
                }
                catch (Exception ex)
                {
                    Log.Debug(
                        ex,
                        "Error stopping network device {Device}.",
                        registration.DisplayName);
                }

                try
                {
                    device.Close();
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Error closing network device {Device}.",
                        registration.DisplayName);
                }

                try
                {
                    (device as IDisposable)?.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Error disposing network device {Device}.",
                        registration.DisplayName);
                }
            }
            finally
            {
                registration.TerminationCompleted.TrySetResult(true);
            }
        }

        private void TerminateSession(CaptureSession? session)
        {
            if (session is null)
            {
                return;
            }

            if (Interlocked.Exchange(ref session.IsTerminated, 1) != 0)
            {
                session.TerminationCompleted.Task.GetAwaiter().GetResult();
                return;
            }

            try
            {
                session.IsReady = false;

                lock (session.ProcessingLock)
                {
                }

                foreach (var registration in session.Devices)
                {
                    try
                    {
                        TerminateDeviceCapture(registration);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(
                            ex,
                            "Unexpected error terminating network device {Device}.",
                            registration.DisplayName);
                    }
                }
            }
            finally
            {
                session.TerminationCompleted.TrySetResult(true);
            }
        }

        public void StopNetworkListening()
        {
            RequestStopAsync(markPowerSuspended: false, disposeService: false)
                .GetAwaiter()
                .GetResult();
        }

        private Task RequestStopAsync(bool markPowerSuspended, bool disposeService)
        {
            LifecycleRequest request;
            CancellationTokenSource? previousCancellation;

            lock (_lifecycleStateLock)
            {
                if (_disposed)
                {
                    return Task.CompletedTask;
                }

                if (disposeService)
                {
                    _disposed = true;
                }

                if (markPowerSuspended || disposeService)
                {
                    _isPowerSuspended = true;
                }

                if (!_listeningRequested
                    && Volatile.Read(ref _activeSession) is null
                    && _lifecycleCancellation is null
                    && !disposeService)
                {
                    Log.Information("Network listening is already stopped.");
                    return Task.CompletedTask;
                }

                _listeningRequested = false;
                request = CreateLifecycleRequestLocked();
                previousCancellation = ReplaceLifecycleCancellationLocked(request.Cancellation);
            }

            CancelLifecycleRequest(previousCancellation);
            return ApplyStopRequestAsync(request);
        }

        private async Task ApplyStopRequestAsync(LifecycleRequest request)
        {
            var acquiredGate = false;

            try
            {
                await _lifecycleGate.WaitAsync(request.Token).ConfigureAwait(false);
                acquiredGate = true;

                if (!IsCurrentRequest(request, listeningRequested: false))
                {
                    return;
                }

                Log.Information("Stopping network listening...");
                var session = Interlocked.Exchange(ref _activeSession, null);
                TerminateSession(session);
                Log.Information("Network listening stopped successfully.");
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
            {
                Log.Debug("Network listener stop was superseded by a newer lifecycle request.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error stopping network listening");
            }
            finally
            {
                if (acquiredGate)
                {
                    _lifecycleGate.Release();
                }

                CompleteLifecycleRequest(request, failedStart: false);
            }
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    switch (e.Mode)
                    {
                        case PowerModes.Suspend:
                            Log.Information("System is entering sleep/hibernate mode. Stopping network listening.");
                            RequestStopAsync(markPowerSuspended: true, disposeService: false)
                                .GetAwaiter()
                                .GetResult();
                            break;
                        case PowerModes.Resume:
                            Log.Information("System is resuming from sleep/hibernate. Starting network listening.");
                            _ = RequestResumeAsync();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error handling power mode change event");
                }
            }
        }

        private void RestartNetworkListener()
        {
            _ = RequestStartAsync(forceRestart: true);
        }

        private Task RequestResumeAsync()
        {
            lock (_lifecycleStateLock)
            {
                if (_disposed)
                {
                    return Task.CompletedTask;
                }

                _isPowerSuspended = false;
            }

            return RequestStartAsync(forceRestart: true);
        }

        public void Dispose()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            }

            _idleService.OnDetectedIdle -= RestartNetworkListener;

            RequestStopAsync(markPowerSuspended: true, disposeService: true)
                .GetAwaiter()
                .GetResult();

            Log.Information("Disposed {type}!", nameof(NetworkListenerService));
        }

        private LifecycleRequest CreateLifecycleRequestLocked()
        {
            var cancellation = new CancellationTokenSource();
            return new LifecycleRequest(
                ++_lifecycleGeneration,
                cancellation,
                cancellation.Token);
        }

        private CancellationTokenSource? ReplaceLifecycleCancellationLocked(
            CancellationTokenSource cancellation)
        {
            var previousCancellation = _lifecycleCancellation;
            _lifecycleCancellation = cancellation;
            return previousCancellation;
        }

        private static void CancelLifecycleRequest(CancellationTokenSource? cancellation)
        {
            if (cancellation is null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        private bool IsCurrentRequest(LifecycleRequest request, bool listeningRequested)
        {
            lock (_lifecycleStateLock)
            {
                return _lifecycleGeneration == request.Generation
                    && _listeningRequested == listeningRequested
                    && (!_disposed || !listeningRequested);
            }
        }

        private bool TryPublishSession(
            LifecycleRequest request,
            CaptureSession session,
            out CaptureSession? replacedSession)
        {
            lock (_lifecycleStateLock)
            {
                if (_lifecycleGeneration != request.Generation
                    || !_listeningRequested
                    || _disposed)
                {
                    replacedSession = null;
                    return false;
                }

                session.IsReady = true;
                replacedSession = Interlocked.Exchange(ref _activeSession, session);
                return true;
            }
        }

        private void CompleteLifecycleRequest(LifecycleRequest request, bool failedStart)
        {
            var disposeCancellation = false;

            lock (_lifecycleStateLock)
            {
                if (_lifecycleGeneration == request.Generation && failedStart)
                {
                    _listeningRequested = false;
                }

                if (ReferenceEquals(_lifecycleCancellation, request.Cancellation))
                {
                    _lifecycleCancellation = null;
                    disposeCancellation = true;
                }
            }

            if (disposeCancellation)
            {
                request.Cancellation.Dispose();
            }
        }

        private sealed class CaptureSession
        {
            private int _isReady;

            public CaptureSession(
                IPhotonReceiver receiver,
                NetworkListenerService listener)
            {
                Receiver = receiver;
                PacketHandler = (sender, packet) => listener.PacketHandler(this, sender, packet);
            }

            public IPhotonReceiver Receiver { get; }
            public PacketArrivalEventHandler PacketHandler { get; }
            public List<CaptureDeviceRegistration> Devices { get; } = new List<CaptureDeviceRegistration>();
            public TaskCompletionSource<bool> TerminationCompleted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public object ProcessingLock { get; } = new object();
            public object? SelectedDevice;
            public int IsTerminated;

            public bool IsReady
            {
                get => Volatile.Read(ref _isReady) != 0;
                set => Volatile.Write(ref _isReady, value ? 1 : 0);
            }
        }

        private sealed class CaptureDeviceRegistration
        {
            public CaptureDeviceRegistration(
                ILiveDevice device,
                PacketArrivalEventHandler packetHandler)
            {
                Device = device;
                PacketHandler = packetHandler;
                DisplayName = GetDeviceDisplayName(device);
            }

            public ILiveDevice Device { get; }
            public PacketArrivalEventHandler PacketHandler { get; }
            public string DisplayName { get; }
            public TaskCompletionSource<bool> TerminationCompleted { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public int IsTerminated;
        }

        private readonly record struct LifecycleRequest(
            long Generation,
            CancellationTokenSource Cancellation,
            CancellationToken Token);

        private readonly record struct CaptureDeviceOpenResult(bool Opened, bool PermissionDenied);
    }
}
