using AlbionDataAvalonia.Ui;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.ViewModels;

public enum ActivityTimePeriod { AllTime, Today, Yesterday, Last7Days, Last30Days, Custom }

/// <summary>
/// The Items tab's time filter: a period that starts at a local midnight, or a custom range of local dates and times.
/// <see cref="Window"/> turns it into the UTC bounds of a query, and <see cref="Changed"/> fires when they may have moved.
/// </summary>
public partial class ActivityTimeFilter : ObservableObject
{
    private static readonly TimeSpan EndOfDay = new(23, 59, 0);

    public IReadOnlyList<Option<ActivityTimePeriod>> PeriodOptions { get; } =
        [new(ActivityTimePeriod.AllTime, "All time"), new(ActivityTimePeriod.Today, "Today"),
         new(ActivityTimePeriod.Yesterday, "Yesterday"), new(ActivityTimePeriod.Last7Days, "Last 7 days"),
         new(ActivityTimePeriod.Last30Days, "Last 30 days"), new(ActivityTimePeriod.Custom, "Custom range")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(IsRangeReversed))]
    private Option<ActivityTimePeriod> selectedPeriod = new(ActivityTimePeriod.AllTime, "All time");
    // A custom range's empty date leaves that side open. Both times are whole minutes and the end's minute counts,
    // so 00:00 to 23:59 covers whole days.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFromDate), nameof(IsRangeReversed))]
    private DateTime? fromDate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeReversed))]
    private TimeSpan? fromTime = TimeSpan.Zero;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasToDate), nameof(IsRangeReversed))]
    private DateTime? toDate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRangeReversed))]
    private TimeSpan? toTime = EndOfDay;

    public event Action? Changed;

    public bool IsCustom => SelectedPeriod.Value == ActivityTimePeriod.Custom;
    public bool HasFromDate => FromDate is not null;
    public bool HasToDate => ToDate is not null;
    public bool IsRangeReversed => IsCustom && CustomWindow() is ({ } start, { } end) && start >= end;

    /// <summary>The UTC bounds on <paramref name="nowUtc"/>'s local day: the start counts, the end doesn't, and null is open.</summary>
    public (DateTime? FromUtc, DateTime? ToUtc) Window(DateTime nowUtc)
    {
        var today = nowUtc.ToLocalTime().Date;
        return SelectedPeriod.Value switch
        {
            ActivityTimePeriod.Today => (Utc(today), null),
            ActivityTimePeriod.Yesterday => (Utc(today.AddDays(-1)), Utc(today)),
            ActivityTimePeriod.Last7Days => (Utc(today.AddDays(-6)), null),
            ActivityTimePeriod.Last30Days => (Utc(today.AddDays(-29)), null),
            ActivityTimePeriod.Custom => CustomWindow(),
            _ => (null, null)
        };
    }

    private (DateTime? FromUtc, DateTime? ToUtc) CustomWindow() =>
        (FromDate is { } startDay ? Utc(startDay.Date + WholeMinutes(FromTime ?? TimeSpan.Zero)) : null,
         ToDate is { } endDay ? Utc(endDay.Date + WholeMinutes(ToTime ?? EndOfDay) + TimeSpan.FromMinutes(1)) : null);

    private static TimeSpan WholeMinutes(TimeSpan time) => TimeSpan.FromMinutes(Math.Floor(time.TotalMinutes));

    private static DateTime Utc(DateTime local) => DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    // A first custom range starts today, so its calendar opens on the current month.
    partial void OnSelectedPeriodChanging(Option<ActivityTimePeriod> value)
    {
        if (value.Value == ActivityTimePeriod.Custom && FromDate is null && ToDate is null) FromDate = DateTime.Today;
    }

    partial void OnSelectedPeriodChanged(Option<ActivityTimePeriod> value) => Changed?.Invoke();
    partial void OnFromDateChanged(DateTime? value) => OnCustomChanged();
    partial void OnFromTimeChanged(TimeSpan? value) => OnCustomChanged();
    partial void OnToDateChanged(DateTime? value) => OnCustomChanged();
    partial void OnToTimeChanged(TimeSpan? value) => OnCustomChanged();

    private void OnCustomChanged()
    {
        if (IsCustom) Changed?.Invoke();
    }
}
