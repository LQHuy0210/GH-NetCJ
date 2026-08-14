using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GH_Net.WPF.ViewModels;

/// <summary>
/// ObservableCollection phát một thông báo Reset cho thao tác theo lô.
/// Tránh làm nghẽn UI thread khi cửa sổ dữ liệu nạp hàng trăm dòng cùng lúc.
/// </summary>
public sealed class BatchObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        NotifyReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        var changed = false;
        foreach (var item in items)
        {
            Items.Add(item);
            changed = true;
        }

        if (changed)
            NotifyReset();
    }

    public void RemovePrefix(int count)
    {
        if (count <= 0)
            return;

        CheckReentrancy();
        count = Math.Min(count, Items.Count);
        for (var index = 0; index < count; index++)
            Items.RemoveAt(0);
        NotifyReset();
    }

    private void NotifyReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
