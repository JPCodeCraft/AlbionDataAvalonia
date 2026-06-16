using Albion.Network;
using AlbionDataAvalonia.Combat;
using AlbionDataAvalonia.Gathering;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Loot;
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
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class NetworkListenerService : IDisposable
    {
        private static readonly object deviceCLeanLock = new object();
        private static readonly object listenLock = new object();
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

        private bool hasCleanedUpDevices = false;
        private bool hasFinishedStartingDevices = false;
        private bool isListening = false;

        private IPhotonReceiver? receiver;
        private CaptureDeviceList? devices;

        public NetworkListenerService(Uploader uploader, PlayerState playerState, SettingsManager settingsManager, MailService mailService, IdleService idleService, TradeService tradeService, AFMUploader afmUploader, ItemsIdsService itemsIdsService, ItemEstimatedMarketValueService itemEstimatedMarketValues, AchievementsService achievementsService, CombatTrackerService combatTracker, GatheringTrackerService gatheringTracker, PartyTrackerService partyTracker, LootTrackerService lootTracker, MobsService mobsService)
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

                var filter = _settingsManager.AppSettings.PacketFilterPortText;

                ReceiverBuilder builder = ReceiverBuilder.Create();

                // ADD HANDLERS HERE
                // EVENTS
                // builder.AddEventHandler(new LeaveEventHandler(_playerState));
                // builder.AddEventHandler(new PlayerCountsEventHandler(_playerState, _afmUploader));
                // builder.AddEventHandler(new CharacterStatsEventHandler());
                builder.AddEventHandler(new NewCharacterEventHandler(_combatTracker));
                builder.AddEventHandler(new NewMobEventHandler(_combatTracker, _mobsService));
                builder.AddEventHandler(new PartyJoinedEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyPlayerJoinedEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyPlayerLeftEventHandler(_partyTracker));
                builder.AddEventHandler(new PartyDisbandedEventHandler(_partyTracker));
                builder.AddEventHandler(new HealthUpdateEventHandler(_combatTracker));
                builder.AddEventHandler(new HealthUpdatesEventHandler(_combatTracker));
                builder.AddEventHandler(new UpdateFameEventHandler(_combatTracker));
                builder.AddEventHandler(new TakeSilverEventHandler(_combatTracker));
                builder.AddEventHandler(new InCombatStateUpdateEventHandler(_combatTracker));
                builder.AddEventHandler(new TimeSyncEventHandler(_combatTracker));
                builder.AddEventHandler(new EstimatedMarketValueUpdateEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues));
                builder.AddEventHandler(new FullAchievementInfoEventHandler(_achievementsService, _playerState, _afmUploader, _settingsManager));
                builder.AddEventHandler(new RedZoneWorldMapEventHandler(_playerState, _uploader));
                builder.AddEventHandler(new HarvestFinishedEventHandler(_gatheringTracker));
                builder.AddEventHandler(new RewardGrantedEventHandler(_gatheringTracker));
                builder.AddEventHandler(new NewLootEventHandler(_lootTracker));
                builder.AddEventHandler(new NewLootChestEventHandler(_lootTracker));
                builder.AddEventHandler(new AttachItemContainerEventHandler(_lootTracker));
                builder.AddEventHandler(new InventoryPutItemEventHandler(_lootTracker));
                builder.AddEventHandler(new OtherGrabbedLootEventHandler(_lootTracker));
                builder.AddEventHandler(new NewSimpleItemEventHandler(_itemsIdsService, _afmUploader, _itemEstimatedMarketValues, _gatheringTracker, _lootTracker));
                builder.AddEventHandler(new NewJournalItemEventHandler(_itemsIdsService, _afmUploader, _lootTracker));
                builder.AddEventHandler(new NewLaborerItemEventHandler(_itemsIdsService, _afmUploader, _lootTracker));
                builder.AddEventHandler(new NewEquipmentItemEventHandler(_itemsIdsService, _afmUploader, _lootTracker));
                builder.AddEventHandler(new NewFurnitureItemEventHandler(_itemsIdsService, _afmUploader, _lootTracker));
                builder.AddEventHandler(new NewKillTrophyItemEventHandler(_itemsIdsService, _afmUploader, _lootTracker));
                builder.AddEventHandler(new NewSiegeBannerItemEventHandler(_itemsIdsService, _afmUploader, _lootTracker));
                // builder.AddEventHandler(new NewEquipmentItemLegendarySoulEventHandler(_playerState));
                // builder.AddEventHandler(new BankVaultInfoEventHandler(_playerState));
#if DEBUG
                builder.AddEventHandler(new DebugEventProbeEventHandler());
#endif
                // RESPONSE
                builder.AddResponseHandler(new AuctionGetLoadoutOffersResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new AuctionGetOffersResponseHandler(_uploader, _playerState, _tradeService));
                builder.AddResponseHandler(new AuctionGetRequestsResponseHandler(_uploader, _playerState, _tradeService));
                builder.AddResponseHandler(new AuctionGetItemAverageStatsResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new JoinResponseHandler(_playerState, _afmUploader, _partyTracker, _lootTracker));
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
                builder.AddRequestHandler(new DebugRequestProbeRequestHandler());
#endif

                receiver = builder.Build();

                if (receiver == null)
                {
                    Log.Error("Failed to create network receiver");
                    return;
                }

                Log.Debug("Starting network device listening");

                devices = CaptureDeviceList.New();

                foreach (var device in devices)
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            Log.Debug("Opening network device: {Device}", device.Description);

                            device.OnPacketArrival += new PacketArrivalEventHandler(PacketHandler);
                            device.Open(new DeviceConfiguration
                            {
                                Mode = DeviceModes.None,
                                ReadTimeout = 5000
                            });
                            device.Filter = filter;
                            device.StartCapture();

                            Log.Debug("Opened network device: {Device} with filter: {Filter}", device.Description, filter);
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("Error initializing device {Device}: {Message}", device.Name, ex.Message);
                        }
                    });
                }

                Log.Information("Listening to Albion network packages!");
                hasFinishedStartingDevices = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error starting network listening");
                isListening = false;
            }
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
    }
}
