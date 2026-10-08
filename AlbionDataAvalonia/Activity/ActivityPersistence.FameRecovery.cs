using AlbionDataAvalonia.Activity.Models;
using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Network.Events;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    private static async Task RestoreKnownFameAwardsAsync(LocalContext context, CancellationToken cancellationToken)
    {
        // Early Activity captures retained valid components but suppressed every nonzero bonus award.
        // Repair only those observations, using the corrected shared decoder and the normal contribution calculator.
        var skipped = 0;
        while (true)
        {
            var rows = await context.ActivityEvents.Where(x => !x.IsRetracted && x.Kind == ActivityEventKind.Fame &&
                    x.Evidence == ActivityEvidence.Supplemental && x.Amount == 0 && x.PayloadVersion == 1)
                .OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.Id).Skip(skipped).Take(250).ToListAsync(cancellationToken);
            if (rows.Count == 0) return;
            var sessions = new Dictionary<Guid, ActivitySession>();
            var encounters = new Dictionary<Guid, ActivityEncounter>();
            foreach (var observation in rows)
            {
                if (!TryRestoreFameAward(observation, out var replacement)) { skipped++; continue; }
                Validate(replacement);
                await ValidateGroupingAsync(context, replacement, cancellationToken);
                await ApplyContributionAsync(context, observation, -1, sessions, encounters, cancellationToken);
                await ApplyContributionAsync(context, replacement, 1, sessions, encounters, cancellationToken);
                context.Entry(observation).CurrentValues.SetValues(replacement);
            }
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
    }

    private static bool TryRestoreFameAward(ActivityEvent observation, out ActivityEvent replacement)
    {
        replacement = observation;
        if (observation.Revision == int.MaxValue) return false;
        try
        {
            var detail = JsonSerializer.Deserialize<FameSnapshot>(observation.DetailsJson);
            if (detail is null || detail.Award is not null || detail.ComponentStates is null || detail.RawFixedPointComponents is null ||
                detail.ComponentStates.GetValueOrDefault((byte)2) != PacketFieldState.Valid ||
                detail.ComponentStates.GetValueOrDefault((byte)17) != PacketFieldState.Valid ||
                detail.ComponentStates.GetValueOrDefault((byte)5) is not (PacketFieldState.Missing or PacketFieldState.Valid) ||
                detail.ComponentStates.GetValueOrDefault((byte)10) is not (PacketFieldState.Missing or PacketFieldState.Valid) ||
                detail.RawFixedPointComponents.GetValueOrDefault((byte)2) is not > 0 ||
                detail.ReportedBonusIncrement is not { } bonus || !double.IsFinite(bonus) || bonus == 0 || bonus < -1)
                return false;

            var rawAward = detail.RawFixedPointComponents[2]!.Value;
            var baseFame = rawAward / 10000d;
            if (!double.IsFinite(detail.PremiumFame) || (detail.PremiumFame != 0 && detail.PremiumFame != baseFame * .5d)) return false;
            var parameters = new Dictionary<byte, object> { [2] = rawAward, [17] = bonus };
            if (detail.ComponentStates.GetValueOrDefault((byte)5) == PacketFieldState.Valid) parameters[5] = detail.PremiumFame > 0;
            else if (detail.PremiumFame != 0) return false;
            if (detail.ComponentStates.GetValueOrDefault((byte)10) == PacketFieldState.Valid)
            {
                if (detail.RawFixedPointComponents.GetValueOrDefault((byte)10) is not { } satchel || satchel < 0) return false;
                parameters[10] = satchel;
            }
            var packet = new UpdateFameEvent(parameters);
            if (packet.ObservedAward is not { } award) return false;
            var json = JsonNode.Parse(observation.DetailsJson) as JsonObject;
            if (json is null) return false;
            json["Award"] = award;
            replacement = observation with
            {
                Revision = observation.Revision + 1,
                Amount = award,
                Evidence = ActivityEvidence.Confirmed,
                DetailsJson = json.ToJsonString()
            };
            return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private sealed record FameSnapshot
    {
        public Dictionary<byte, PacketFieldState>? ComponentStates { get; init; }
        public Dictionary<byte, long?>? RawFixedPointComponents { get; init; }
        public double PremiumFame { get; init; }
        public double? ReportedBonusIncrement { get; init; }
        public double? Award { get; init; }
    }
}
