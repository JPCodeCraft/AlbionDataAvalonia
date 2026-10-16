using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

// HasFameProgress: fame achievements moved with the award, whether or not they decided its category.
internal sealed record ActivityFameMatch(ActivityFameCategory Category, IReadOnlyList<int> AchievementIndexes, bool HasFameProgress = false)
{
    public static ActivityFameMatch Unknown { get; } = new(ActivityFameCategory.Unknown, []);
}

/// <summary>Matches an award to captured progression or kill evidence, never to the active encounter.</summary>
internal sealed partial class ActivityFameCollector(AchievementsService achievements)
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromMilliseconds(250);
    // Each credited achievement takes two reliable messages before the award; spear and shield reach five.
    private const int MaximumSequenceGap = 16;
    private const int MaximumPendingProgress = 64;
    private readonly List<ProgressObservation> pending = new();

    public void Observe(AchievementProgressInfoEvent packet, DateTime at)
    {
        Expire(at);
        if (packet.ActorObjectId is not > 0 || packet.AchievementIndex is not >= 0 ||
            packet.ProgressState != PacketFieldState.Valid || packet.ProgressValues.Count == 0 ||
            packet.MessageIdentity is not { Kind: PhotonMessageKind.Reliable } identity) return;
        if (pending.Count == MaximumPendingProgress) pending.RemoveAt(0);
        pending.Add(new(packet.ActorObjectId.Value, packet.AchievementIndex.Value, at, identity));
    }

    public ActivityFameMatch Match(UpdateFameEvent packet, long? actorObjectId, DateTime at)
    {
        Expire(at);
        if (actorObjectId is not > 0 || packet.MessageIdentity is not { Kind: PhotonMessageKind.Reliable } identity)
        {
            pending.Clear();
            return ActivityFameMatch.Unknown;
        }

        var candidates = pending.Where(x => x.ActorObjectId == actorObjectId && SameStream(x.Identity, identity) &&
            SequenceGap(x.Identity, identity) is > 0 and <= MaximumSequenceGap &&
            x.CapturedAtUtc <= at && at - x.CapturedAtUtc <= MatchWindow).ToArray();
        // One progression observation must never classify a later second award.
        pending.RemoveAll(x => x.ActorObjectId == actorObjectId && SameStream(x.Identity, identity) &&
            SequenceGap(x.Identity, identity) is > 0 and < 0x80000000);
        var hasFameProgress = candidates.Any(x => achievements.GetAchievementInfoByIndex(x.AchievementIndex).MissionTypes
            .Any(type => type.EndsWith("fame", StringComparison.OrdinalIgnoreCase)));
        var unmatched = new ActivityFameMatch(ActivityFameCategory.Unknown, [], hasFameProgress);
        if (candidates.Any(x => achievements.GetAchievementInfoByIndex(x.AchievementIndex).MissionTypes
            .Any(type => type.EndsWith("fame", StringComparison.OrdinalIgnoreCase) &&
                type.ToLowerInvariant() is not ("gatherfame" or "fishingfame" or "killmobfame"))))
            return unmatched;
        var categorized = candidates.Select(x => (Observation: x, Category: Classify(x.AchievementIndex)))
            .Where(x => x.Category != ActivityFameCategory.Unknown).ToArray();
        var categories = categorized.Select(x => x.Category).Distinct().ToArray();
        if (categories.Length != 1) return unmatched;
        var category = categories[0];
        if (packet.ComponentStates.GetValueOrDefault((byte)12) == PacketFieldState.Invalid) return unmatched;
        return new(category, categorized.Select(x => x.Observation.AchievementIndex).Distinct().ToArray(), true);
    }

    // The saved award includes Quick Learn's bonus, as the game reports it; shown fame leaves it out.
    public static double? Award(UpdateFameEvent packet) =>
        packet.QuickLearnFame is { } quickLearn ? packet.ObservedAward + quickLearn : packet.ObservedAward;

    private ActivityFameCategory Classify(int index)
    {
        var types = achievements.GetAchievementInfoByIndex(index).MissionTypes ?? [];
        var categories = types.Select(type => type.ToLowerInvariant() switch
        {
            "gatherfame" => ActivityFameCategory.Gathering,
            "fishingfame" => ActivityFameCategory.Fishing,
            "killmobfame" => ActivityFameCategory.Pve,
            _ => ActivityFameCategory.Unknown
        }).Where(x => x != ActivityFameCategory.Unknown).Distinct().ToArray();
        return categories.Length == 1 ? categories[0] : ActivityFameCategory.Unknown;
    }

    public void Expire(DateTime at)
    {
        pending.RemoveAll(x => at - x.CapturedAtUtc > MatchWindow);
        pendingAwards.RemoveAll(x => at - x.Value.CapturedAtUtc > MatchWindow);
        pendingKills.RemoveAll(x => at - x.CapturedAtUtc > MatchWindow);
        killAwards.RemoveAll(x => at - x.CapturedAtUtc > MobKillWindow);
        mobKills.RemoveAll(x => at - x > MobKillWindow);
    }

    public void Reset()
    {
        pending.Clear();
        pendingAwards.Clear();
        pendingKills.Clear();
        killAwards.Clear();
        mobKills.Clear();
    }

    private static uint SequenceGap(PhotonMessageIdentity earlier, PhotonMessageIdentity later) =>
        unchecked((uint)later.ReliableSequence - (uint)earlier.ReliableSequence);
    private static bool SameStream(PhotonMessageIdentity a, PhotonMessageIdentity b) =>
        a.ReceiverGeneration == b.ReceiverGeneration && a.DirectionalConnection == b.DirectionalConnection &&
        a.PeerId == b.PeerId && a.Challenge == b.Challenge && a.Channel == b.Channel;
    private sealed record ProgressObservation(long ActorObjectId, int AchievementIndex, DateTime CapturedAtUtc,
        PhotonMessageIdentity Identity);
}
