namespace AlbionDataAvalonia.Settings;

using Serilog;
using Serilog.Events;
using System;
using System.ComponentModel;

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
                OnPropertyChanged(nameof(StartHidden));
                Log.Information("Start hidden set to {StartHidden}", _startHidden);
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

    private int maxHashQueueSize = 30;
    public int MaxHashQueueSize
    {
        get => maxHashQueueSize;
        set
        {
            if (maxHashQueueSize != value)
            {
                maxHashQueueSize = value;
                OnPropertyChanged(nameof(MaxHashQueueSize));
                Log.Information("Max hash queue size set to {MaxHashQueueSize}", maxHashQueueSize);
            }
        }
    }

    private int maxLogCount = 50;
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

    private int mailsPerPage = 200;
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

    private double salesTax = 0.04;
    public double SalesTax
    {
        get => salesTax;
        set
        {
            if (salesTax != value)
            {
                salesTax = value;
                OnPropertyChanged(nameof(SalesTax));
                Log.Information("Sales tax set to {SalesTax}", salesTax);
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
                if (AppData.ListSinkLevelSwitch != null)
                {
                    AppData.ListSinkLevelSwitch.MinimumLevel = logLevel;
                    Log.Information("Log level set to {LogLevel}", logLevel);
                }
                OnPropertyChanged(nameof(LogLevel));
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

