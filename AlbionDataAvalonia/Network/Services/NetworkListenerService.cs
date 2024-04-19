using Albion.Network;
using AlbionDataAvalonia.Network.Handlers;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using PacketDotNet;
using Serilog;
using SharpPcap;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class NetworkListenerService : IDisposable
    {
        private readonly SemaphoreSlim deviceCleanSemaphore = new SemaphoreSlim(1, 1);

        private readonly Uploader _uploader;
        private readonly PlayerState _playerState;
        private readonly SettingsManager _settingsManager;

        private bool hasInitializedDevices = false;
        private bool hasCleanedUpDevices = false;

        private IPhotonReceiver? receiver;
        private CaptureDeviceList? devices;

        public NetworkListenerService(Uploader uploader, PlayerState playerState, SettingsManager settingsManager)
        {
            _uploader = uploader;
            _playerState = playerState;
            _settingsManager = settingsManager;
        }

        public async Task Run()
        {
            if (NpCapInstallationChecker.IsNpCapInstalled() == false)
            {
                Log.Error("NpCap is not installed, please install it to use this application");
                return;
            }

            ReceiverBuilder builder = ReceiverBuilder.Create();

            //ADD HANDLERS HERE
            //EVENTS
            //builder.AddEventHandler(new LeaveEventHandler(_playerState));
            //RESPONSE
            builder.AddResponseHandler(new AuctionGetOffersResponseHandler(_uploader, _playerState));
            builder.AddResponseHandler(new AuctionGetRequestsResponseHandler(_uploader, _playerState));
            builder.AddResponseHandler(new AuctionGetItemAverageStatsResponseHandler(_uploader, _playerState));
            builder.AddResponseHandler(new JoinResponseHandler(_playerState));
            builder.AddResponseHandler(new AuctionGetGoldAverageStatsResponseHandler(_uploader));
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
                        InitializeDevice(device);
                    }
                    catch (Exception ex)
                    {
                        Log.Information("Error initializing device {Device}: {Message}", device.Name, ex.Message);
                    }
                });
                hasInitializedDevices = true;
            }

            Log.Information("Listening to Albion network packages!");

            return;
        }

        private void PacketHandler(object? sender, PacketCapture e)
        {
            if (receiver == null)
            {
                Log.Error("Receiver is null");
                return;
            }
            if (!hasInitializedDevices)
            {
                Log.Debug("All devices not initialized, ignoring packet");
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
                        deviceCleanSemaphore.Wait();
                        try
                        {
                            if (!hasCleanedUpDevices)
                            {
                                var currentDevice = e.Device;
                                var tasks = devices.Where(device => device != currentDevice)
                                                   .Select(CloseDevice);
                                Task.WhenAll(tasks).Wait();
                                hasCleanedUpDevices = true;
                            }
                        }
                        finally
                        {
                            deviceCleanSemaphore.Release();
                        }
                    }

                    var srcIp = (packet.ParentPacket as IPPacket)?.SourceAddress?.ToString();

                    if (string.IsNullOrEmpty(srcIp))
                    {
                        Log.Verbose("Packet Source IP null or empty, ignoring");
                        return;
                    }
                    var server = _settingsManager.AppSettings.AlbionServers.SingleOrDefault(x => srcIp.Contains(x.HostIp));
                    if (server is not null)
                    {
                        Log.Verbose("Packet from {server} server from IP {ip}", server.Name, srcIp);
                        _playerState.AlbionServer = server;
                    }
                    Task.Run(() => receiver.ReceivePacket(packet.PayloadData));
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex.Message);
            }
        }
        private void InitializeDevice(ILiveDevice device)
        {
            Log.Information("Opening network device: {Device}", device.Description);

            device.OnPacketArrival += new PacketArrivalEventHandler(PacketHandler);
            device.Open(new DeviceConfiguration
            {
                Mode = DeviceModes.MaxResponsiveness,
                ReadTimeout = 5000
            });
            var ips = _settingsManager.AppSettings.AlbionServers.Select(s => $"host {s.HostIp}");
            var filter = $"({string.Join(" or ", ips)}) and {_settingsManager.AppSettings.PacketFilterPortText}";
            device.Filter = filter;
            device.StartCapture();

            Log.Information("Listening on network device: {Device} with filter: {Filter}", device.Description, filter);
        }

        private Task CloseDevice(ICaptureDevice device)
        {
            return Task.Run(() =>
            {
                try
                {
                    device.StopCapture();
                    device.Close();
                    Log.Information("Closing network device: {Device}", device.Description);
                }
                catch (Exception ex)
                {
                    Log.Information("Error closing device {Device}: {Message}", device.Name, ex.Message);
                }
            });
        }

        private void Cleanup()
        {
            // Close network devices, flush logs, etc.
            if (devices is not null)
            {
                foreach (var device in devices)
                {
                    device.StopCapture();
                    device.Close();
                    Log.Debug("Close... {Device}", device.Description);
                }
            }
        }

        public void Dispose()
        {
            Cleanup();
            Log.Information("Stopped {type}!", nameof(NetworkListenerService));
        }
    }
}
