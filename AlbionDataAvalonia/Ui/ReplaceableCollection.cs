using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace AlbionDataAvalonia.Ui;

/// <summary>
/// An observable list whose items can be swapped all at once. A grid bound to it refills the rows it already has,
/// which costs far less than the new rows a grid builds when it is handed a new list.
/// </summary>
public sealed class ReplaceableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces every item and raises a single reset.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
