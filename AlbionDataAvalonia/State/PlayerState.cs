using AlbionDataAvalonia.Locations;
using AlbionDataAvalonia.Locations.Models;
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

        private AlbionLocation location = AlbionLocations.Unset;
        private string playerName = string.Empty;
        private AlbionServer? albionServer = null;
        private bool isInGame = false;
        private bool hasEncryptedData = false;

        private bool uploadToAfmOnly = false;

        public MarketHistoryInfo[] MarketHistoryIDLookup { get; init; }
        public ulong CacheSize => 8192;
        private Queue<string> SentDataHashs = new Queue<string>();

        public event EventHandler<PlayerStateEventArgs>? OnPlayerStateChanged;

        public event Action<int>? OnUploadedMarketOffersCountChanged;
        public event Action<int>? OnUploadedMarketRequestsCountChanged;
        public event Action<ConcurrentDictionary<Timescale, int>>? OnUploadedHistoriesCountDicChanged;
        public event Action<int>? OnUploadedGoldHistoriesCountChanged;
        public event Action<ConcurrentDictionary<UploadStatus, int>>? OnUploadStatusCountDicChanged;

        public int UploadedMarketOffersCount { get; set; }
        public int UploadedMarketRequestsCount { get; set; }
        public ConcurrentDictionary<Timescale, int> UploadedHistoriesCountDic { get; set; } = new();
        public int UploadedGoldHistoriesCount { get; set; }

        private ConcurrentDictionary<UploadStatus, int> UploadStatusCountDic { get; set; } = new()
        {
            [UploadStatus.Success] = 0,
            [UploadStatus.Failed] = 0,
            [UploadStatus.Skipped] = 0
        };

        public int UserObjectId { get; set; }

        private ConcurrentQueue<long> PowSolveTimes { get; } = new();
        public double PowSolveTimeAverage => PowSolveTimes.Count > 0 ? PowSolveTimes.Average() : 0;

        public DateTime LastPacketTime { get; set; } = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

        public AlbionLocation Location
        {
            get => location;
            set
            {
                location = value;
                Log.Information("Player location set to {Location}", Location.FriendlyName);
                InvokePlayerStateChanged();
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
                InvokePlayerStateChanged();
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
                InvokePlayerStateChanged();
            }
        }
        public bool UploadToAfmOnly
        {
            get => uploadToAfmOnly;
            set
            {
                if (uploadToAfmOnly == value) return;
                uploadToAfmOnly = value;
                InvokePlayerStateChanged();
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
                    Log.Verbose("Player is {InGame}", isInGame ? "in game" : "not in game");
                    if (!isInGame)
                    {
                        this.hasEncryptedData = false;
                    }
                    InvokePlayerStateChanged();
                }
                return isInGame;
            }
        }

        public bool HasEncryptedData
        {
            get => hasEncryptedData;
            set
            {
                if (hasEncryptedData == value) return;
                hasEncryptedData = value;
                InvokePlayerStateChanged();
            }
        }

        private void InvokePlayerStateChanged()
        {
            OnPlayerStateChanged?.Invoke(this, new PlayerStateEventArgs(Location, PlayerName, AlbionServer, IsInGame, HasEncryptedData, UploadToAfmOnly));
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
            ProcessUploadStatus(e.UploadStatus, e.MarketUpload.Identifier);
            if (e.UploadStatus != UploadStatus.Success) return;

            int offersCount = e.MarketUpload.Orders.Count(o => o.AuctionType == AuctionType.offer);
            int requestsCount = e.MarketUpload.Orders.Count(o => o.AuctionType == AuctionType.request);

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
        }

        public void MarketHistoryUploadHandler(object? sender, MarketHistoriesUploadEventArgs e)
        {
            ProcessUploadStatus(e.UploadStatus, e.MarketHistoriesUpload.Identifier);
            if (e.UploadStatus != UploadStatus.Success) return;

            if (!UploadedHistoriesCountDic.ContainsKey(e.MarketHistoriesUpload.Timescale))
            {
                UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale] = 0;
            }

            int historyCount = e.MarketHistoriesUpload.MarketHistories.Count;

            UploadedHistoriesCountDic[e.MarketHistoriesUpload.Timescale] += historyCount;
            OnUploadedHistoriesCountDicChanged?.Invoke(UploadedHistoriesCountDic);
        }

        public void GoldPriceUploadHandler(object? sender, GoldPriceUploadEventArgs e)
        {
            ProcessUploadStatus(e.UploadStatus, e.GoldPriceUpload.Identifier);
            if (e.UploadStatus != UploadStatus.Success) return;

            int goldHistoriesCount = e.GoldPriceUpload.Prices.Length;

            UploadedGoldHistoriesCount += goldHistoriesCount;
            OnUploadedGoldHistoriesCountChanged?.Invoke(UploadedGoldHistoriesCount);
        }

        private void ProcessUploadStatus(UploadStatus status, Guid identifier)
        {
            UploadStatusCountDic[status]++;
            OnUploadStatusCountDicChanged?.Invoke(UploadStatusCountDic);
            Log.Verbose("Upload status: {status} => accounted for. Identifier: {identifier}", status, identifier);
        }

        public bool CheckLocationIsSet()
        {
            if (location == AlbionLocations.Unknown || location == AlbionLocations.Unset)
            {
                Log.Warning("Player location is not set. Please change maps.");
                return false;
            }
            else return true;
        }

        public void AddSentDataHash(string hash)
        {
            try
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
            catch (Exception ex)
            {
                Log.Error(ex, "Error adding hash to queue: {ex}", ex.Message);
            }
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
            InvokePlayerStateChanged();
        }
    }
}
