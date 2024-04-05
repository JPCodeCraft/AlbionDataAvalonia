namespace AlbionDataAvalonia.Settings;

using System.ComponentModel;

public class UserSettings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _startHidden = false;
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

    private float _threadLimitPercentage = 0.3f;
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
            }
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

