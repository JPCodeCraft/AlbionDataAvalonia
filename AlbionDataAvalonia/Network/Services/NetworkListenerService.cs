using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot;
using AlbionDataAvalonia.Legendary;
using AlbionDataAvalonia.Network.Handlers;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Party;
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
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class NetworkListenerService : IDisposable
    {
        private static readonly object deviceCLeanLock = new object();
        private static readonly object listenLock = new object();
        private const string MacOSCapturePermissionSetupScriptName = "setup-capture-permissions.sh";
        private readonly HashSet<string> _unknownServerIps = new HashSet<string>();

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
        private readonly LootTrackerService _lootTracker;
        private readonly MobsService _mobsService;
        private readonly LegendaryItemTrackerService _legendaryTracker;

        private bool hasCleanedUpDevices = false;
        private bool hasFinishedStartingDevices = false;
        private bool isListening = false;

        private IPhotonReceiver? receiver;
        private CaptureDeviceList? devices;

        public event EventHandler? MacOSCapturePermissionSetupRequiredChanged;
        public bool IsMacOSCapturePermissionSetupRequired { get; private set; }

        public NetworkListenerService(Uploader uploader, PlayerState playerState, SettingsManager settingsManager, MailService mailService, IdleService idleService, TradeService tradeService, AFMUploader afmUploader, ItemsIdsService itemsIdsService, ItemEstimatedMarketValueService itemEstimatedMarketValues, AchievementsService achievementsService, CombatTrackerService combatTracker, GatheringTrackerService gatheringTracker, PartyTrackerService partyTracker, LootTrackerService lootTracker, MobsService mobsService, LegendaryItemTrackerService legendaryTracker)
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
        }

        public async Task StartNetworkListeningAsync()
        {
            lock (listenLock)
            {
                if (isListening)
                {
                    Log.Information("Network listening is already active.");
                    return;
                }
                isListening = true;
            }
            try
            {
                //AWAIT SOME SECONDS FOR NETWORK STUFF TO BE READY
                Log.Information($"Waiting {_settingsManager.AppSettings.NetworkDevicesStartDelaySecs} seconds for network drivers to be ready");
                await Task.Delay(_settingsManager.AppSettings.NetworkDevicesStartDelaySecs * 1000);

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
                builder.AddEventHandler(new NewCharacterEventHandler(_combatTracker, _partyTracker));
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
                receiver = builder.Build();

                if (receiver == null)
                {
                    Log.Error("Failed to create network receiver");
                    return;
                }

                Log.Debug("Starting network device listening");

                devices = CaptureDeviceList.New();
                if (!devices.Any())
                {
                    Log.Error("No network capture devices were found.");
                    isListening = false;
                    return;
                }

                var openedDeviceCount = 0;
                var failedDeviceCount = 0;
                var sawPermissionDenied = false;
                foreach (var device in devices)
                {
                    var result = await Task.Run(() => TryStartDeviceCapture(device, filter));
                    if (result.Opened)
                    {
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
                    hasFinishedStartingDevices = false;
                    isListening = false;
                    receiver = null;
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

                SetMacOSCapturePermissionSetupRequired(false);
                Log.Information("Listening to Albion network packages!");
                hasFinishedStartingDevices = true;
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

                isListening = false;
            }
        }

        private CaptureDeviceOpenResult TryStartDeviceCapture(ILiveDevice device, string filter)
        {
            try
            {
                Log.Debug("Opening network device: {Device}", GetDeviceDisplayName(device));

                device.OnPacketArrival += new PacketArrivalEventHandler(PacketHandler);
                device.Open(new DeviceConfiguration
                {
                    Mode = DeviceModes.None,
                    ReadTimeout = 5000
                });
                device.Filter = filter;
                device.StartCapture();

                Log.Debug("Opened network device: {Device} with filter: {Filter}", GetDeviceDisplayName(device), filter);
                return new CaptureDeviceOpenResult(true, false);
            }
            catch (Exception ex)
            {
                device.OnPacketArrival -= PacketHandler;

                try
                {
                    device.Close();
                }
                catch
                {
                }

                Log.Warning(ex, "Error initializing network device {Device}.", GetDeviceDisplayName(device));
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

        private void PacketHandler(object? sender, PacketCapture e)
        {
            if (receiver == null)
            {
                Log.Error("Receiver is null");
                return;
            }
            if (!hasFinishedStartingDevices)
            {
                Log.Debug("Not all devices have finished starting yet");
                return;
            }
            try
            {
                _playerState.LastPacketTime = DateTime.UtcNow;

                UdpPacket packet = Packet.ParsePacket(e.GetPacket().LinkLayerType, e.GetPacket().Data).Extract<UdpPacket>();
                if (packet != null)
                {
                    if (!hasCleanedUpDevices && devices != null)
                    {
                        lock (deviceCLeanLock)
                        {
                            if (!hasCleanedUpDevices && devices != null)
                            {
                                foreach (var device in devices)
                                {
                                    if (device != e.Device)
                                    {
                                        Task.Run(() =>
                                        {
                                            try
                                            {
                                                TerminateDeviceCapture(device);
                                            }
                                            catch (Exception ex)
                                            {
                                                Log.Debug("Error closing device {Device}: {Message}", device.Name, ex.Message);
                                            }
                                        });
                                    }
                                }
                                hasCleanedUpDevices = true;
                            }
                        }
                    }

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
                    var packetStatus = receiver.ReceivePacket(packet.PayloadData);
                    if (packetStatus == PacketStatus.Encrypted)
                    {
                        _playerState.HasEncryptedData = true;
                        Log.Warning("Encrypted packet received! You can't see market orders!");
                    }
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

        private void TerminateDeviceCapture(ILiveDevice device)
        {
            if (device != null)
            {
                Log.Debug("Closing network device: {Device}", device.Description);
                try
                {
                    device.StopCapture();
                    device.Close();
                    device.OnPacketArrival -= PacketHandler;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error closing device {Device}", device.Description);
                }
            }
        }

        private void TerminateAllDeviceCaptures()
        {
            if (devices is not null)
            {
                foreach (var device in devices)
                {
                    try
                    {
                        TerminateDeviceCapture(device);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error terminating device capture for {Device}", device.Description);
                    }
                }
            }
        }

        public void StopNetworkListening()
        {
            lock (listenLock)
            {
                if (!isListening)
                {
                    Log.Information("Network listening is already stopped.");
                    return;
                }
                isListening = false;
            }
            Log.Information("Stopping network listening...");

            // Stop and close all capture devices
            TerminateAllDeviceCaptures();

            // Reset the receiver
            receiver = null;

            // Reset the devices list
            devices = null;

            // Reset flags
            hasCleanedUpDevices = false;
            hasFinishedStartingDevices = false;

            Log.Information("Network listening stopped successfully.");
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
                            StopNetworkListening();
                            break;
                        case PowerModes.Resume:
                            Log.Information("System is resuming from sleep/hibernate. Starting network listening.");
                            Task.Run(StartNetworkListeningAsync);
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
            StopNetworkListening();
            Task.Run(StartNetworkListeningAsync);
        }

        public void Dispose()
        {
            StopNetworkListening();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
            }
            _idleService.OnDetectedIdle -= RestartNetworkListener;
            Log.Information("Disposed {type}!", nameof(NetworkListenerService));
        }

        private readonly record struct CaptureDeviceOpenResult(bool Opened, bool PermissionDenied);
    }
}
