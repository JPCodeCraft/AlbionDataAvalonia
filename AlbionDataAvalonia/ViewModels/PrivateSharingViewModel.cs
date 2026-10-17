using AlbionDataAvalonia.Network.Models;
using AlbionDataAvalonia.Network.Services;
using AlbionDataAvalonia.Ui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

/// <summary>
/// Settings › Uploads &amp; sharing: the friends who see your private market orders. The list lives on the AFM
/// account, so it loads when you sign in and every change saves straight away.
/// </summary>
public sealed partial class PrivateSharingViewModel : ViewModelBase
{
    public const int MaxFriends = 5;
    private const string SignInStatus = "Sign in to manage private sharing.";

    private readonly AFMUploader? afmUploader;
    private readonly IClipboardService? clipboard;

    public PrivateSharingViewModel()
    {
    }

    public PrivateSharingViewModel(AFMUploader? afmUploader, IClipboardService? clipboard)
    {
        this.afmUploader = afmUploader;
        this.clipboard = clipboard;
    }

    public ObservableCollection<PrivateOrderShareEntryViewModel> Friends { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RedactedUserId), nameof(IsSignedIn), nameof(CanEdit), nameof(CanAdd))]
    private string userId = "";

    [ObservableProperty] private string friendInput = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNormalStatus))]
    private string status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanAdd))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnresolvedEntries), nameof(HasNormalStatus))]
    private string unresolvedEntriesText = "";

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(UserId);
    public bool CanEdit => IsSignedIn && !IsBusy;
    public bool CanAdd => CanEdit && Friends.Count < MaxFriends;
    public bool HasUnresolvedEntries => !string.IsNullOrWhiteSpace(UnresolvedEntriesText);

    /// <summary>A status that isn't the warning about entries that couldn't be added.</summary>
    public bool HasNormalStatus => !string.IsNullOrWhiteSpace(Status) && !HasUnresolvedEntries;

    public string LimitText => $"{Friends.Count} / {MaxFriends} friends";
    public string RedactedUserId => Redact(UserId);

    /// <summary>Called when the signed-in account changes; an empty ID means signed out.</summary>
    public void SetUser(string? signedInUserId)
    {
        var next = signedInUserId ?? "";
        if (IsSignedIn && string.Equals(UserId, next, StringComparison.Ordinal)) return;
        UserId = next;
        if (!IsSignedIn)
        {
            Friends.Clear();
            OnFriendsChanged();
            FriendInput = "";
            UnresolvedEntriesText = "";
            Status = SignInStatus;
            return;
        }

        _ = LoadAsync(showStatus: false);
    }

    [RelayCommand]
    private async Task CopyUserId()
    {
        if (clipboard is not null && IsSignedIn) await clipboard.SetTextAsync(UserId);
    }

    [RelayCommand]
    private async Task Add()
    {
        var value = FriendInput.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Friends.Count >= MaxFriends)
        {
            Status = $"You can share with up to {MaxFriends} friends.";
            return;
        }

        if (Friends.Any(x => string.Equals(DedupeKey(x.Value), DedupeKey(value), StringComparison.Ordinal)))
        {
            FriendInput = "";
            return;
        }

        await SaveAsync(Friends.Select(x => x.Value).Append(value));
    }

    [RelayCommand]
    private async Task Remove(PrivateOrderShareEntryViewModel? entry)
    {
        if (entry is not null) await SaveAsync(Friends.Where(x => !ReferenceEquals(x, entry)).Select(x => x.Value));
    }

    [RelayCommand]
    private Task Load() => LoadAsync(showStatus: true);

    private async Task SaveAsync(IEnumerable<string> friends)
    {
        if (!IsSignedIn || afmUploader is null)
        {
            Status = SignInStatus;
            return;
        }

        var expectedUserId = UserId;
        await RunAsync(async () =>
        {
            UnresolvedEntriesText = "";
            Status = "Saving sharing list…";
            var response = await afmUploader.SavePrivateOrderSharesAsync(friends);
            if (!IsCurrentUser(expectedUserId)) return;
            if (response is null)
            {
                Status = "Could not save the friends list. Try again in a moment.";
                return;
            }

            Apply(response.SharedUsers);
            UnresolvedEntriesText = string.Join(", ", response.UnresolvedEntries);
            FriendInput = "";
            Status = response.UnresolvedEntries.Count == 0 ? "Sharing list updated." : UnableToAddMessage(response.UnresolvedEntries);
        });
    }

    private async Task LoadAsync(bool showStatus)
    {
        if (!IsSignedIn || afmUploader is null)
        {
            if (showStatus) Status = SignInStatus;
            return;
        }

        var expectedUserId = UserId;
        await RunAsync(async () =>
        {
            var response = await afmUploader.GetPrivateOrderSharesAsync();
            if (!IsCurrentUser(expectedUserId)) return;
            if (response is null)
            {
                if (showStatus) Status = "Failed to load private sharing settings.";
                return;
            }

            Apply(response.SharedUsers);
            UnresolvedEntriesText = "";
            if (showStatus) Status = "Private sharing settings loaded.";
        });
    }

    // A reply for an account that has since signed out or changed must not fill in the new account's list.
    private bool IsCurrentUser(string expectedUserId) =>
        IsSignedIn && !string.IsNullOrWhiteSpace(expectedUserId) && string.Equals(UserId, expectedUserId, StringComparison.Ordinal);

    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(IEnumerable<PrivateOrderShareEntry> entries)
    {
        Friends.Clear();
        foreach (var entry in entries) Friends.Add(new PrivateOrderShareEntryViewModel(entry.Value, entry.Type, entry.Resolved));
        OnFriendsChanged();
    }

    private void OnFriendsChanged()
    {
        OnPropertyChanged(nameof(LimitText));
        OnPropertyChanged(nameof(CanAdd));
    }

    private static string UnableToAddMessage(IReadOnlyCollection<string> entries) => entries.Count == 1
        ? $"Unable to add {entries.First()}. Check the user ID, email or AFM username. If the entry is ambiguous, use the email instead."
        : $"Unable to add {string.Join(", ", entries)}. Check the user IDs, emails or AFM usernames. For ambiguous entries, use email instead.";

    private static string DedupeKey(string value) => value.Contains('@', StringComparison.Ordinal)
        ? $"email:{value.Trim().ToLowerInvariant()}"
        : $"userId:{value.Trim()}";

    // Friends add you by your ID; the screen shows only its ends, in case it is screenshotted.
    private static string Redact(string value)
    {
        const int visibleStart = 4;
        const int visibleEnd = 4;
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (value.Length <= visibleStart + visibleEnd) return new string('*', value.Length);
        return $"{value[..visibleStart]}{new string('*', value.Length - visibleStart - visibleEnd)}{value[^visibleEnd..]}";
    }
}

public sealed class PrivateOrderShareEntryViewModel
{
    public PrivateOrderShareEntryViewModel(string value, string type, bool resolved)
    {
        Value = value;
        Type = type;
        Resolved = resolved;
    }

    public string Value { get; }
    public string Type { get; }
    public bool Resolved { get; }
    public string TypeLabel => Type.ToLowerInvariant() switch
    {
        "email" => "Email",
        "username" => "AFM username",
        _ => "User ID"
    };
}
