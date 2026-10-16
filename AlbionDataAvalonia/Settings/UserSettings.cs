namespace AlbionDataAvalonia.Settings;

using Serilog;
using Serilog.Events;
using System;
using System.ComponentModel;

public enum StartupWindowMode
{
    ShowMainWindow,
    MinimizedToTaskbar,
    HiddenToTray
}

public class UserSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _startHidden = true;
    public bool StartHidden
    {
        get => _startHidden;
        set
        {
            if (_startHidden != value)
            {
                _startHidden = value;
                if (value)
                {
                    StartupWindowMode = StartupWindowMode.HiddenToTray;
                }
                else if (StartupWindowMode == StartupWindowMode.HiddenToTray)
                {
                    StartupWindowMode = StartupWindowMode.ShowMainWindow;
                }

                OnPropertyChanged(nameof(StartHidden));
                Log.Information("Start hidden set to {StartHidden}", _startHidden);
            }
        }
    }

    private bool startWithWindows = true;
    public bool StartWithWindows
    {
        get => startWithWindows;
        set
        {
            if (startWithWindows != value)
            {
                startWithWindows = value;
                OnPropertyChanged(nameof(StartWithWindows));
                Log.Information("Start with Windows set to {StartWithWindows}", startWithWindows);
            }
        }
    }

    private bool minimizeButtonMinimizesToTray = false;
    public bool MinimizeButtonMinimizesToTray
    {
        get => minimizeButtonMinimizesToTray;
        set
        {
            if (minimizeButtonMinimizesToTray != value)
            {
                minimizeButtonMinimizesToTray = value;
                OnPropertyChanged(nameof(MinimizeButtonMinimizesToTray));
                Log.Information("Minimize button minimizes to tray set to {MinimizeButtonMinimizesToTray}", minimizeButtonMinimizesToTray);
            }
        }
    }

    private StartupWindowMode startupWindowMode = StartupWindowMode.ShowMainWindow;
    public StartupWindowMode StartupWindowMode
    {
        get => startupWindowMode;
        set
        {
            if (startupWindowMode != value)
            {
                startupWindowMode = value;
                var nextStartHidden = startupWindowMode == StartupWindowMode.HiddenToTray;
                if (_startHidden != nextStartHidden)
                {
                    _startHidden = nextStartHidden;
                    OnPropertyChanged(nameof(StartHidden));
                }

                OnPropertyChanged(nameof(StartupWindowMode));
                Log.Information("Startup window mode set to {StartupWindowMode}", startupWindowMode);
            }
        }
    }

    private bool shutDownOnClose = false;
    public bool ShutDownOnClose
    {
        get => shutDownOnClose;
        set
        {
            if (shutDownOnClose != value)
            {
                shutDownOnClose = value;
                OnPropertyChanged(nameof(ShutDownOnClose));
                Log.Information("Shut down on close set to {ShutDownOnClose}", shutDownOnClose);
            }
        }
    }

    private bool joinBetaProgram = false;
    public bool JoinBetaProgram
    {
        get => joinBetaProgram;
        set
        {
            if (joinBetaProgram != value)
            {
                joinBetaProgram = value;
                OnPropertyChanged(nameof(JoinBetaProgram));
                Log.Information("Join beta program set to {JoinBetaProgram}", joinBetaProgram);
            }
        }
    }

    private int _desiredThreadCount = 1;
    public int DesiredThreadCount
    {
        get => _desiredThreadCount;
        set
        {
            if (_desiredThreadCount != value)
            {
                _desiredThreadCount = value;
                if (_desiredThreadCount > MaxThreadCount)
                {
                    _desiredThreadCount = MaxThreadCount;
                }
                else if (_desiredThreadCount < 1)
                {
                    _desiredThreadCount = 1;
                }
                OnPropertyChanged(nameof(DesiredThreadCount));
                Log.Information("Desired thread count set to {DesiredThreadCount}", _desiredThreadCount);
            }
        }
    }

    public int MaxThreadCount => Environment.ProcessorCount;

    private int maxLogCount = 1000;
    public int MaxLogCount
    {
        get => maxLogCount;
        set
        {
            if (maxLogCount != value)
            {
                maxLogCount = value;
                OnPropertyChanged(nameof(MaxLogCount));
                Log.Information("Max log count set to {MaxLogCount}", maxLogCount);
            }
        }
    }

    private int mailsPerPage = 1000;
    public int MailsPerPage
    {
        get => mailsPerPage;
        set
        {
            if (mailsPerPage != value)
            {
                mailsPerPage = value;
                OnPropertyChanged(nameof(MailsPerPage));
                Log.Information("Mails per page set to {MailsPerPage}", mailsPerPage);
            }
        }
    }

    private int tradesToShow = 1000;
    public int TradesToShow
    {
        get => tradesToShow;
        set
        {
            if (tradesToShow != value)
            {
                tradesToShow = value;
                OnPropertyChanged(nameof(TradesToShow));
                Log.Information("Trades to show set to {TradesToShow}", tradesToShow);
            }
        }
    }

    private bool uploadToAfmOnly;
    public bool UploadToAfmOnly
    {
        get => uploadToAfmOnly;
        set
        {
            if (uploadToAfmOnly == value) return;
            uploadToAfmOnly = value;
            OnPropertyChanged(nameof(UploadToAfmOnly));
            Log.Information("Private Mode set to {Enabled}", value);
        }
    }

    private bool contributeToPublic;
    public bool ContributeToPublic
    {
        get => contributeToPublic;
        set
        {
            if (contributeToPublic == value) return;
            contributeToPublic = value;
            OnPropertyChanged(nameof(ContributeToPublic));
            Log.Information("Share with Other AFM Users set to {Enabled}", value);
        }
    }

    private bool shareWithFriends;
    public bool ShareWithFriends
    {
        get => shareWithFriends;
        set
        {
            if (shareWithFriends == value) return;
            shareWithFriends = value;
            OnPropertyChanged(nameof(ShareWithFriends));
            Log.Information("Share with Friends set to {Enabled}", value);
        }
    }

    private bool uploadSpecsToAfm = true;
    public bool UploadSpecsToAfm
    {
        get => uploadSpecsToAfm;
        set
        {
            if (uploadSpecsToAfm != value)
            {
                uploadSpecsToAfm = value;
                OnPropertyChanged(nameof(UploadSpecsToAfm));
                Log.Information("Upload specs to AFM set to {UploadSpecsToAfm}", uploadSpecsToAfm);
            }
        }
    }

    private bool afmIslandTrackerEnabled = true;
    public bool AfmIslandTrackerEnabled
    {
        get => afmIslandTrackerEnabled;
        set
        {
            if (afmIslandTrackerEnabled != value)
            {
                afmIslandTrackerEnabled = value;
                OnPropertyChanged(nameof(AfmIslandTrackerEnabled));
                Log.Information("AFM island tracker enabled set to {AfmIslandTrackerEnabled}", afmIslandTrackerEnabled);
            }
        }
    }

    private bool activityRecordingEnabled = true;
    public bool ActivityRecordingEnabled
    {
        get => activityRecordingEnabled;
        set
        {
            if (activityRecordingEnabled == value) return;
            activityRecordingEnabled = value;
            OnPropertyChanged(nameof(ActivityRecordingEnabled));
            Log.Information("Activity recording enabled set to {ActivityRecordingEnabled}", activityRecordingEnabled);
        }
    }

    // The streamer window shows only the sections chosen here.
    private bool streamerShowSilver = true;
    public bool StreamerShowSilver { get => streamerShowSilver; set => SetStreamerOption(ref streamerShowSilver, value, nameof(StreamerShowSilver)); }

    private bool streamerShowCombat = true;
    public bool StreamerShowCombat { get => streamerShowCombat; set => SetStreamerOption(ref streamerShowCombat, value, nameof(StreamerShowCombat)); }

    private bool streamerShowGathering;
    public bool StreamerShowGathering { get => streamerShowGathering; set => SetStreamerOption(ref streamerShowGathering, value, nameof(StreamerShowGathering)); }

    private bool streamerShowChart;
    public bool StreamerShowChart { get => streamerShowChart; set => SetStreamerOption(ref streamerShowChart, value, nameof(StreamerShowChart)); }

    private bool streamerPartyTotals;
    public bool StreamerPartyTotals { get => streamerPartyTotals; set => SetStreamerOption(ref streamerPartyTotals, value, nameof(StreamerPartyTotals)); }

    private bool streamerChromaKey;
    public bool StreamerChromaKey { get => streamerChromaKey; set => SetStreamerOption(ref streamerChromaKey, value, nameof(StreamerChromaKey)); }

    // 0 combat, 1 gathering, 2 fame, matching the activity chart modes.
    private int streamerChartModeIndex;
    public int StreamerChartModeIndex
    {
        get => streamerChartModeIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (streamerChartModeIndex == clamped) return;
            streamerChartModeIndex = clamped;
            OnPropertyChanged(nameof(StreamerChartModeIndex));
            Log.Information("Streamer chart mode set to {StreamerChartModeIndex}", streamerChartModeIndex);
        }
    }

    // 0 small (headline numbers only), 1 medium, 2 large (with breakdowns).
    private int streamerSizeIndex = 1;
    public int StreamerSizeIndex
    {
        get => streamerSizeIndex;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (streamerSizeIndex == clamped) return;
            streamerSizeIndex = clamped;
            OnPropertyChanged(nameof(StreamerSizeIndex));
            Log.Information("Streamer size set to {StreamerSizeIndex}", streamerSizeIndex);
        }
    }

    private void SetStreamerOption(ref bool field, bool value, string propertyName)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
        Log.Information("{Setting} set to {Value}", propertyName, value);
    }

    private bool disableAwakeningItemsTracker;
    public bool DisableAwakeningItemsTracker
    {
        get => disableAwakeningItemsTracker;
        set
        {
            if (disableAwakeningItemsTracker != value)
            {
                disableAwakeningItemsTracker = value;
                OnPropertyChanged(nameof(DisableAwakeningItemsTracker));
                Log.Information(
                    "Disable awakening items tracker set to {DisableAwakeningItemsTracker}",
                    disableAwakeningItemsTracker);
            }
        }
    }

    private LogEventLevel logLevel = LogEventLevel.Information;
    public LogEventLevel LogLevel
    {
        get => logLevel;
        set
        {
            if (logLevel != value)
            {
                logLevel = value;
                Log.Information("Log level set to {LogLevel}", logLevel);
                OnPropertyChanged(nameof(LogLevel));
                OnPropertyChanged(nameof(LogLevelIndex));
            }
        }
    }

    public int LogLevelIndex
    {
        get => (int)logLevel;
        set
        {
            var clamped = Math.Clamp(value, 0, 5);
            var nextLevel = (LogEventLevel)clamped;
            if (logLevel != nextLevel)
            {
                LogLevel = nextLevel;
            }
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void UpdateFrom(UserSettings newSettings)
    {
        var properties = typeof(UserSettings).GetProperties();
        foreach (var property in properties)
        {
            if (property.CanWrite)
            {
                var value = property.GetValue(newSettings);
                property.SetValue(this, value);
            }
        }
    }
}
