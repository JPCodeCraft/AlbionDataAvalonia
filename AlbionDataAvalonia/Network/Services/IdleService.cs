using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Network.Services
{
    public class IdleService
    {
        private readonly SettingsManager _settingsManager;
        private readonly PlayerState _playerState;

        private DateTime lastIdleCheck = DateTime.Now;
        public event Action OnDetectedIdle = delegate { };

        public IdleService(SettingsManager settingsManager, PlayerState playerState)
        {
            _settingsManager = settingsManager;
            _playerState = playerState;
        }

        public Task ExecuteAsync()
        {
            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(TimeSpan.FromMinutes(_settingsManager.AppSettings.NetworkDevicesIdleCheckMinutes));

                    Log.Debug($"Idle check thread running, last packet time: {_playerState.LastPacketTime}, last idle check: {lastIdleCheck}");

                    if ((DateTime.Now - _playerState.LastPacketTime) > TimeSpan.FromMinutes(_settingsManager.AppSettings.NetworkDevicesIdleMinutes))
                    {
                        Log.Debug("Idle check triggered, last packet time: {LastPacketTime}", _playerState.LastPacketTime);
                        OnDetectedIdle?.Invoke();
                    }
                }
            });

            return Task.CompletedTask;
        }
    }
}
