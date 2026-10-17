using AlbionDataAvalonia.Activity;
using AlbionDataAvalonia.Activity.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>Paged reads and in-place row updates shared by the Activity tabs.</summary>
internal static class ActivityReads
{
    // Cancels the previous read of one kind and returns the token for the next.
    public static CancellationToken BeginRead(ref CancellationTokenSource source)
    {
        source.Cancel();
        source.Dispose();
        source = new CancellationTokenSource();
        return source.Token;
    }

    // A refresh rereads every page on view; Load more reads only the next page.
    public static async Task<ActivityPage<T>> ReadPagesAsync<T>(Func<int, Task<ActivityPage<T>>> readPage, int lastPage, bool append)
    {
        if (append) return await readPage(lastPage);
        var pages = await Task.WhenAll(Enumerable.Range(0, lastPage + 1).Select(readPage));
        return new(pages.SelectMany(page => page.Items).ToArray(), pages[^1].HasMore);
    }

    public static Task<ActivityPage<ActivityEvent>> ReadEventRowsAsync(ActivityPersistence persistence, ActivityQuery query, int lastPage, bool append,
        CancellationToken cancellationToken) => append
        ? persistence.GetEventsAsync(query, lastPage, cancellationToken)
        : persistence.GetEventsThroughPageAsync(query, lastPage, cancellationToken);

    // Rows are updated in place, so a list keeps its selection and scroll position across refreshes.
    public static void ApplyHeaderRows<TRow, TSource, TKey>(ObservableCollection<TRow> rows, IReadOnlyList<TSource> sources,
        Func<TRow, TKey> rowId, Func<TSource, TKey> sourceId, Func<TSource, TRow> create, Action<TRow, TSource> update, bool append) where TKey : notnull
    {
        if (!append)
        {
            var wantedIds = sources.Select(sourceId).ToHashSet();
            for (var index = rows.Count - 1; index >= 0; index--)
                if (!wantedIds.Contains(rowId(rows[index]))) rows.RemoveAt(index);
        }
        var byId = rows.ToDictionary(rowId);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var id = sourceId(source);
            if (!append && index < rows.Count && EqualityComparer<TKey>.Default.Equals(rowId(rows[index]), id)) update(rows[index], source);
            else if (byId.TryGetValue(id, out var existing))
            {
                update(existing, source);
                if (!append) rows.Move(rows.IndexOf(existing), index);
            }
            else
            {
                var row = create(source);
                byId.Add(id, row);
                if (append) rows.Add(row);
                else rows.Insert(index, row);
            }
        }
        if (!append) while (rows.Count > sources.Count) rows.RemoveAt(rows.Count - 1);
    }

    public static void ApplyEventRows<T>(ObservableCollection<T> rows, IReadOnlyList<T> replacements, Func<T, ActivityEvent> eventOf, bool append)
    {
        if (append)
        {
            var indices = rows.Select((row, index) => (Id: eventOf(row).Id, Index: index)).ToDictionary(value => value.Id, value => value.Index);
            foreach (var replacement in replacements)
            {
                var id = eventOf(replacement).Id;
                if (indices.TryGetValue(id, out var index))
                {
                    if (eventOf(rows[index]) != eventOf(replacement)) rows[index] = replacement;
                }
                else { indices.Add(id, rows.Count); rows.Add(replacement); }
            }
            return;
        }
        for (var i = 0; i < replacements.Count; i++)
        {
            if (i >= rows.Count) rows.Add(replacements[i]);
            else if (eventOf(rows[i]) != eventOf(replacements[i])) rows[i] = replacements[i];
        }
        while (rows.Count > replacements.Count) rows.RemoveAt(rows.Count - 1);
    }

    public static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
