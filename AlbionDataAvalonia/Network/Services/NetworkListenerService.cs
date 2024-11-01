using Albion.Network;
using AlbionDataAvalonia.Network.Handlers;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Microsoft.Win32;
using PacketDotNet;
using PhotonPackageParser;
using Serilog;
using SharpPcap;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class NetworkListenerService : IDisposable
    {
        private static readonly object deviceCLeanLock = new object();
        private static readonly object listenLock = new object();

        private readonly Uploader _uploader;
        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;
        private readonly MailService _mailService;
        private readonly IdleService _idleService;

        private bool hasCleanedUpDevices = false;
        private bool hasFinishedStartingDevices = false;
        private bool isListening = false;

        private IPhotonReceiver? receiver;
        private CaptureDeviceList? devices;

        public NetworkListenerService(Uploader uploader, PlayerState playerState, SettingsManager settingsManager, MailService mailService, IdleService idleService)
        {
            _uploader = uploader;
            _playerState = playerState;
            _settingsManager = settingsManager;
            _mailService = mailService;
            _idleService = idleService;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
            }

            _idleService.OnDetectedIdle += RestartNetworkListener;
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

                var ips = AlbionServers.GetAll().Select(s => $"host {s.HostIp}");
                var filter = $"({string.Join(" or ", ips)}) and {_settingsManager.AppSettings.PacketFilterPortText}";

                ReceiverBuilder builder = ReceiverBuilder.Create();

                //ADD HANDLERS HERE
                //EVENTS
                //builder.AddEventHandler(new LeaveEventHandler(_playerState));
                //RESPONSE
                builder.AddResponseHandler(new AuctionGetLoadoutOffersResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new AuctionGetOffersResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new AuctionGetRequestsResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new AuctionGetItemAverageStatsResponseHandler(_uploader, _playerState));
                builder.AddResponseHandler(new JoinResponseHandler(_playerState));
                builder.AddResponseHandler(new AuctionGetGoldAverageStatsResponseHandler(_uploader));
                builder.AddResponseHandler(new GetMailInfosResponseHandler(_playerState, _mailService, _settingsManager));
                builder.AddResponseHandler(new ReadMailResponseHandler(_playerState, _mailService));
                //REQUEST
                builder.AddRequestHandler(new AuctionGetItemAverageStatsRequestHandler(_playerState));

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
                    var server = AlbionServers.GetAll().SingleOrDefault(x => srcIp.Contains(x.HostIp));
                    if (server is not null)
                    {
                        //Log.Verbose("Packet from {server} server from IP {ip}", server.Name, srcIp);
                        _playerState.AlbionServer = server;
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
                Log.Debug(ex.Message);
            }
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
