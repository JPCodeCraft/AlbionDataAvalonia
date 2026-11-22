using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Ui;
using AlbionDataAvalonia.Ui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.ViewModels;

public enum ActivityChatMetric { Damage, Healing }

/// <summary>
/// Copies your party's damage or healing as one line for the game chat, like a DPS meter:
/// "Damage · Dungeon (12m 30s): 1. Liam 1.2M 33% 1.6k DPS · 2. …". The button shows whether it worked.
/// </summary>
public sealed partial class ActivityChatCopy : ObservableObject
{
    public const int MaximumPlayers = 5;
    private const string ReadyLabel = "Copy for chat";
    private readonly IClipboardService? clipboard;
    private int version;

    [ObservableProperty] private string label = ReadyLabel;

    public ActivityChatCopy(IClipboardService? clipboard) => this.clipboard = clipboard;

    public bool IsAvailable => clipboard is not null;

    /// <summary>Builds the line with <paramref name="read"/> and copies it; null from it means there was nothing to copy.</summary>
    public async Task CopyAsync(Func<Task<string?>> read)
    {
        if (clipboard is null) return;
        var current = ++version;
        string result;
        try
        {
            result = await read() is { Length: > 0 } text ? await clipboard.SetTextAsync(text) ? "Copied" : "Couldn't copy" : "Nothing to copy";
        }
        catch (Exception error)
        {
            Log.Warning(error, "Could not build the chat text.");
            result = "Couldn't copy";
        }
        Label = result;
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (current == version) Label = ReadyLabel;
    }

    /// <summary>
    /// One line with the top players of you and your party by the metric, their share and their rate over the combat time.
    /// Null when nobody in the party did any.
    /// </summary>
    public static string? Build(ActivityChatMetric metric, string title, double combatSeconds, IEnumerable<ActivityActorTotal> totals)
    {
        var players = totals.Where(x => x.Relationship is ActorRelationship.Self or ActorRelationship.Party)
            .GroupBy(x => x.ActorId)
            .Select(g => (Name: ActivityDisplay.ActorName(g.First().Name), Amount: g.Sum(x => metric == ActivityChatMetric.Damage ? x.DamageDealt : x.HealingDone)))
            .Where(x => x.Amount > 0)
            .OrderByDescending(x => x.Amount)
            .ToArray();
        if (players.Length == 0) return null;
        var total = players.Sum(x => x.Amount);
        var rate = metric == ActivityChatMetric.Damage ? "DPS" : "HPS";
        var heading = $"{metric} · {(string.IsNullOrWhiteSpace(title) ? "Activity" : title.Trim())}" +
            (combatSeconds > 0 ? $" ({UiFormat.Duration(combatSeconds)})" : "");
        var ranked = players.Take(MaximumPlayers).Select((x, index) =>
            $"{index + 1}. {x.Name} {UiFormat.Compact(x.Amount)} {Math.Round(x.Amount / total * 100):0}%" +
            (combatSeconds > 0 ? $" {UiFormat.Compact(x.Amount / combatSeconds)} {rate}" : ""));
        return $"{heading}: {string.Join(" · ", ranked)}";
    }
}
