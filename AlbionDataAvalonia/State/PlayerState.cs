using AlbionDataAvalonia.Network.Events;
using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Settings;
using AlbionDataAvalonia.State.Events;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.State
{
    public class PlayerState
    {
        private readonly SettingsManager _settingsManager;

        private AlbionLocation location = AlbionLocations.Unknown;
        private string playerName = string.Empty;
        private AlbionServer? albionServer = null;
        private bool isInGame = false;

        public MarketHistoryInfo[] MarketHistoryIDLookup { get; init; }
        public ulong CacheSize => 8192;
        private Queue<string> SentDataHashs = new Queue<string>();

        public event EventHandler<PlayerStateEventArgs>? OnPlayerStateChanged;

        public event Action<int>? OnUploadedMarketOffersCountChanged;
        public event Action<int>? OnUploadedMarketRequestsCountChanged;
        public event Action<ConcurrentDictionary<Timescale, int>>? OnUploadedHistoriesCountDicChanged;
        public event Action<int>? OnUploadedGoldHistoriesCountChanged;

        public int UploadedMarketOffersCount { get; set; }
        public int UploadedMarketRequestsCount { get; set; }
        public ConcurrentDictionary<Timescale, int> UploadedHistoriesCountDic { get; set; } = new();
        public int UploadedGoldHistoriesCount { get; set; }

        public ConcurrentDictionary<UploadStatus, int> UploadStatusCountDic { get; set; } = new()
        {
            [UploadStatus.Success] = 0,
            [UploadStatus.Failed] = 0,
            [UploadStatus.Skipped] = 0
        };

        public int UserObjectId { get; set; }

        private ConcurrentQueue<long> PowSolveTimes { get; } = new();
        public double PowSolveTimeAverage => PowSolveTimes.Count > 0 ? PowSolveTimes.Average() : 0;

        public DateTime LastPacketTime { get; set; }

        public AlbionLocation Location
        {
            get => location;
            set
            {
                location = value;
                Log.Information("Player location set to {Location}", Location.FriendlyName);
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, IsInGame));
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
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, IsInGame));
            }
        }
        public AlbionServer? AlbionServer
        {
            get => albionServer;
            set
            {
                if (albionServer == value) return;
                albionServer = value;
                if (albionServer != null)
                {
                    Log.Information("Server set to {Server}", albionServer.Name);
                }
                OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, IsInGame));
            }
        }
        public bool IsInGame
        {
            get
            {
                var result = (DateTime.UtcNow - LastPacketTime) < TimeSpan.FromSeconds(10);
                if (isInGame != result)
                {
                    isInGame = result;
                    Log.Debug("Player is {InGame}", isInGame ? "in game" : "not in game");
                    OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, isInGame));
                }
                return isInGame;
            }
        }

        public PlayerState(SettingsManager settingsManager)
        {
            MarketHistoryIDLookup = new MarketHistoryInfo[CacheSize];
            _settingsManager = settingsManager;

            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += OnTimerElapsed;
            timer.Start();
        }
        private void OnTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            _ = IsInGame;
        }

        public void MarketUploadHandler(object? sender, MarketUploadEventArgs e)
        {
            ProcessUploadStatus(e.UploadStatus);
            if (e.UploadStatus != UploadStatus.Success) return;

            int offersCount = e.MarketUpload.Orders.Count(o => o.AuctionType == "offer");
            int requestsCount = e.MarketUpload.Orders.Count(o => o.AuctionType == "request");

            if (offersCount > 0)
            {
                UploadedMarketOffersCount += offersCount;
                OnUploadedMarketOffersCountChanged?.Invoke(UploadedMarketOffersCount);
            }
            if (requestsCount > 0)
            {
                UploadedMarketRequestsCount += requestsCount;
                OnUploadedMarketRequestsCountChanged?.Invoke(UploadedMarketRequestsCount);
            }

            Log.Information("Market upload complete. {Offers} offers, {Requests} requests", offersCount, requestsCount);
        }

        public void MarketHistoryUploadHandler(object? sender, MarketHistoriesUploadEventArgs e)
        {
            ProcessUploadStatus(e.UploadStatus);
            if (e.UploadStatus != UploadStatus.Success) return;

            if (!UploadedHistoriesCountDic.ContainsKey(e.MarketHistoriesUpload.Timescale))
            {
                UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale] = 0;
            }

            int historyCount = e.MarketHistoriesUpload.MarketHistories.Count;

            UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale] += historyCount;
            OnUploadedHistoriesCountDicChanged?.Invoke(UploadedHistoriesCountDic);
            Log.Information("Market history upload complete. [{Timescale}] => {count} histories", historyCount, e.MarketHistoriesUpload.Timescale);
        }

        public void GoldPriceUploadHandler(object? sender, GoldPriceUploadEventArgs e)
        {
            ProcessUploadStatus(e.UploadStatus);
            if (e.UploadStatus != UploadStatus.Success) return;

            int goldHistoriesCount = e.GoldPriceUpload.Prices.Length;

            UploadedGoldHistoriesCount += goldHistoriesCount;
            OnUploadedGoldHistoriesCountChanged?.Invoke(UploadedGoldHistoriesCount);
            Log.Information("Gold price upload complete. {count} histories", goldHistoriesCount);
        }

        private void ProcessUploadStatus(UploadStatus status)
        {
            UploadStatusCountDic[status]++;
            Log.Debug("Upload status: {status} => accounted for", status);
        }

        public bool CheckLocationIsSet()
        {
            if (location == AlbionLocations.Unknown)
            {
                Log.Debug($"Player location is not set. Please change maps.");
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

        public bool CheckOkToUpload()
        {
            return CheckLocationIsSet() && IsInGame && AlbionServer != null;
        }

        public void AddPowSolveTime(long time)
        {
            PowSolveTimes.Enqueue(time);
            while (PowSolveTimes.Count > 50)
            {
                PowSolveTimes.TryDequeue(out _);
            }
            OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, IsInGame));
        }
    }
}
