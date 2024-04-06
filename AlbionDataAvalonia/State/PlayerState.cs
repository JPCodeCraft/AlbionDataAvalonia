using AlbionData.Models;
using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State.Events;
using Serilog;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.State
{
    public class PlayerState
    {
        private readonly SettingsManager _settingsManager;

        private Location location = 0;
        private string playerName = string.Empty;
        private AlbionServer? albionServer = null;
        private bool isInGame = false;
        private int uploadQueueSize = 0;

        public MarketHistoryInfo[] MarketHistoryIDLookup { get; init; }
        public ulong CacheSize => 8192;
        private Queue<string> SentDataHashs = new Queue<string>();

        public event EventHandler<PlayerStateEventArgs>? OnPlayerStateChanged;

        public event Action<int>? OnUploadedMarketOffersCountChanged;
        public event Action<int>? OnUploadedMarketRequestsCountChanged;
        public event Action<Dictionary<Timescale, int>>? OnUploadedHistoriesCountDicChanged;
        public event Action<int>? OnUploadedGoldHistoriesCountChanged;

        public int UploadedMarketOffersCount { get; set; }
        public int UploadedMarketRequestsCount { get; set; }
        public Dictionary<Timescale, int> UploadedHistoriesCountDic { get; set; } = new();
        public int UploadedGoldHistoriesCount { get; set; }

        public int UserObjectId { get; set; }

        public DateTime LastPacketTime { get; set; }

        public Location Location
        {
            get => location;
            set
            {
                location = value;
                Log.Information("Player location set to {Location}", Location.ToString());
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, UploadQueueSize, IsInGame));
            }
        }
        public string PlayerName
        {
            get => playerName;
            set
            {
                if (playerName == value) return;
                playerName = value;
                Log.Information("Player name set to {PlayerName}", PlayerName);
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, UploadQueueSize, IsInGame));
            }
        }
        public AlbionServer? AlbionServer
        {
            get => albionServer;
            set
            {
                if (albionServer == value) return;
                albionServer = value;
                Log.Information("Server set to {Server}", AlbionServer.Name);
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, UploadQueueSize, IsInGame));
            }
        }
        public bool IsInGame
        {
            get
            {
                var result = DateTime.UtcNow - LastPacketTime < TimeSpan.FromSeconds(5);
                if (isInGame != result)
                {
                    isInGame = result;
                    if (!isInGame)
                    {
                        AlbionServer = null;
                        PlayerName = string.Empty;
                        Location = 0;
                    }
                    OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, UploadQueueSize, IsInGame));
                }
                return isInGame;
            }
        }
        public int UploadQueueSize
        {
            get => uploadQueueSize;
            set
            {
                uploadQueueSize = value;
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, UploadQueueSize, IsInGame));
            }
        }

        public PlayerState(SettingsManager settingsManager)
        {
            MarketHistoryIDLookup = new MarketHistoryInfo[CacheSize];
            _settingsManager = settingsManager;

            var timer = new System.Timers.Timer(2000);
            timer.Elapsed += OnTimerElapsed;
            timer.Start();
        }
        private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var isInGame = IsInGame;
        }

        public void MarketUploadHandler(object? sender, MarketUploadEventArgs e)
        {
            if (e.MarketUpload.Orders[0].AuctionType == "offer")
            {
                UploadedMarketOffersCount += e.MarketUpload.Orders.Count;
                OnUploadedMarketOffersCountChanged?.Invoke(UploadedMarketOffersCount);
            }
            else
            {
                UploadedMarketRequestsCount += e.MarketUpload.Orders.Count;
                OnUploadedMarketRequestsCountChanged?.Invoke(UploadedMarketRequestsCount);
            }
            Log.Information("Market upload complete. {Offers} offers, {Requests} requests", UploadedMarketOffersCount, UploadedMarketRequestsCount);
        }

        public void MarketHistoryUploadHandler(object? sender, MarketHistoriesUploadEventArgs e)
        {
            if (!UploadedHistoriesCountDic.ContainsKey(e.MarketHistoriesUpload.Timescale))
            {
                UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale] = 0;
            }

            UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale] += e.MarketHistoriesUpload.MarketHistories.Count;
            OnUploadedHistoriesCountDicChanged?.Invoke(UploadedHistoriesCountDic);
            Log.Information("Market history upload complete. {count} histories [{Timescale}] ", UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale], e.MarketHistoriesUpload.Timescale);
        }

        public void GoldPriceUploadHandler(object? sender, GoldPriceUploadEventArgs e)
        {
            UploadedGoldHistoriesCount += e.GoldPriceUpload.Prices.Length;
            OnUploadedGoldHistoriesCountChanged?.Invoke(UploadedGoldHistoriesCount);
            Log.Information("Gold price upload complete. {count} histories", UploadedGoldHistoriesCount);
        }

        public bool CheckLocationIDIsSet()
        {
            if (location == 0 || !Enum.IsDefined(typeof(AlbionData.Models.Location), Location))
            {
                Log.Warning($"Player location is not set. Please change maps.");
                return false;
            }
            else return true;
        }

        public void AddSentDataHash(string hash)
        {
            if (hash == null || hash.Length == 0 || SentDataHashs.Contains(hash)) return;

            if (_settingsManager.UserSettings.MaxHashQueueSize == 0)
            {
                SentDataHashs.Clear();
                return;
            }

            while (SentDataHashs.Count >= _settingsManager.UserSettings.MaxHashQueueSize)
            {
                SentDataHashs.Dequeue();
            }
            SentDataHashs.Enqueue(hash);
        }

        public bool CheckHashInQueue(string hash)
        {
            bool result = SentDataHashs.Contains(hash);
            return result;
        }
    }
}
