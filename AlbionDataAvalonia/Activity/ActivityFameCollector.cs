using Albion.Network;
using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.Items.Services;
using AlbionDataAvalonia.Network.Events;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Activity;

internal sealed record ActivityFameMatch(ActivityFameCategory Category, IReadOnlyList<int> AchievementIndexes,
    double? Award = null, bool UsesEnhancedAward = false)
{
    public static ActivityFameMatch Unknown { get; } = new(ActivityFameCategory.Unknown, []);
}

/// <summary>Matches an award to preceding progression evidence, never to the active encounter.</summary>
internal sealed class ActivityFameCollector(AchievementsService achievements)
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromMilliseconds(250);
    private const int MaximumSequenceGap = 4;
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
        if (candidates.Any(x => achievements.GetAchievementInfoByIndex(x.AchievementIndex).MissionTypes
            .Any(type => type.EndsWith("fame", StringComparison.OrdinalIgnoreCase) &&
                type.ToLowerInvariant() is not ("gatherfame" or "fishingfame" or "killmobfame"))))
            return ActivityFameMatch.Unknown;
        var categorized = candidates.Select(x => (Observation: x, Category: Classify(x.AchievementIndex)))
            .Where(x => x.Category != ActivityFameCategory.Unknown).ToArray();
        var categories = categorized.Select(x => x.Category).Distinct().ToArray();
        if (categories.Length != 1) return ActivityFameMatch.Unknown;
        var category = categories[0];
        var indexes = categorized.Select(x => x.Observation.AchievementIndex).Distinct().ToArray();
        if (category is ActivityFameCategory.Gathering or ActivityFameCategory.Fishing)
        {
            var states = packet.ComponentStates;
            if (states.GetValueOrDefault((byte)12) == PacketFieldState.Invalid
                || states.GetValueOrDefault((byte)13) == PacketFieldState.Invalid)
                return ActivityFameMatch.Unknown;
            var hasEnhancedEvidence = packet.RawFixedPointComponents.GetValueOrDefault((byte)12) is > 0
                || packet.RawFixedPointComponents.GetValueOrDefault((byte)13) is > 0;
            if (hasEnhancedEvidence)
            {
                if (packet.ObservedProgressionAward is not { } enhancedAward
                    || packet.ObservedAward is { } ordinaryAward && enhancedAward < ordinaryAward)
                    return ActivityFameMatch.Unknown;
                return new(category, indexes, enhancedAward, UsesEnhancedAward: true);
            }
        }
        return new(category, indexes, packet.ObservedAward);
    }

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

    public void Expire(DateTime at) => pending.RemoveAll(x => at - x.CapturedAtUtc > MatchWindow);
    public void Reset() => pending.Clear();

    private static uint SequenceGap(PhotonMessageIdentity earlier, PhotonMessageIdentity later) =>
        unchecked((uint)later.ReliableSequence - (uint)earlier.ReliableSequence);
    private static bool SameStream(PhotonMessageIdentity a, PhotonMessageIdentity b) =>
        a.ReceiverGeneration == b.ReceiverGeneration && a.DirectionalConnection == b.DirectionalConnection &&
        a.PeerId == b.PeerId && a.Challenge == b.Challenge && a.Channel == b.Channel;
    private sealed record ProgressObservation(long ActorObjectId, int AchievementIndex, DateTime CapturedAtUtc,
        PhotonMessageIdentity Identity);
}
