namespace AlbionDataAvalonia.Settings;

using Serilog;
using Serilog.Events;
using System;
using System.ComponentModel;

public class UserSettings : INotifyPropertyChanged
{
    public const int MinCombatEncounterRetentionLimit = 50;
    public const int MaxCombatEncounterRetentionLimit = 10000;
    public const int DefaultCombatEncounterRetentionLimit = 1000;

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
                OnPropertyChanged(nameof(StartHidden));
                Log.Information("Start hidden set to {StartHidden}", _startHidden);
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

    private bool disableCombatTracker = false;
    public bool DisableCombatTracker
    {
        get => disableCombatTracker;
        set
        {
            if (disableCombatTracker != value)
            {
                disableCombatTracker = value;
                OnPropertyChanged(nameof(DisableCombatTracker));
                Log.Information("Disable combat tracker set to {DisableCombatTracker}", disableCombatTracker);
            }
        }
    }

    private int combatEncounterRetentionLimit = DefaultCombatEncounterRetentionLimit;
    public int CombatEncounterRetentionLimit
    {
        get => combatEncounterRetentionLimit;
        set
        {
            var clamped = Math.Clamp(value, MinCombatEncounterRetentionLimit, MaxCombatEncounterRetentionLimit);
            if (combatEncounterRetentionLimit != clamped)
            {
                combatEncounterRetentionLimit = clamped;
                OnPropertyChanged(nameof(CombatEncounterRetentionLimit));
                Log.Information("Combat encounter retention limit set to {CombatEncounterRetentionLimit}", combatEncounterRetentionLimit);
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

