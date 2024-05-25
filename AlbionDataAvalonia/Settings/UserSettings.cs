namespace AlbionDataAvalonia.Settings;

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
            }
        }
    }

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

