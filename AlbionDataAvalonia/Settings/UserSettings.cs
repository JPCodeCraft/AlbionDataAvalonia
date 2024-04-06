namespace AlbionDataAvalonia.Settings;

using System.ComponentModel;

public class UserSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _startHidden;
    public bool StartHidden
    {
        get => _startHidden;
        set
        {
            if (_startHidden != value)
            {
                _startHidden = value;
                OnPropertyChanged(nameof(StartHidden));
            }
        }
    }

    private float _threadLimitPercentage;
    public float ThreadLimitPercentage
    {
        get => _threadLimitPercentage;
        set
        {
            if (_threadLimitPercentage != value)
            {
                _threadLimitPercentage = value;
                OnPropertyChanged(nameof(ThreadLimitPercentage));
            }
        }
    }

    private int maxHashQueueSize;
    public int MaxHashQueueSize
    {
        get => maxHashQueueSize;
        set
        {
            if (maxHashQueueSize != value)
            {
                maxHashQueueSize = value;
                OnPropertyChanged(nameof(MaxHashQueueSize));
            }
        }
    }

    private int maxLogCount;
    public int MaxLogCount
    {
        get => maxLogCount;
        set
        {
            if (maxLogCount != value)
            {
                maxLogCount = value;
                OnPropertyChanged(nameof(MaxLogCount));
            }
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

