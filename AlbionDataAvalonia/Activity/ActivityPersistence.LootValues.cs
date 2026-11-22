using AlbionDataAvalonia.Activity.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Activity;

public sealed partial class ActivityPersistence
{
    /// <summary>
    /// Values loot saved with both EMVs at the chosen one (Settings › Trackers), and moves the session and fight
    /// totals with it. Loot saved before both EMVs were kept keeps the value it has.
    /// </summary>
    public Task RevalueLootAsync(ActivityLootEmv choice, CancellationToken cancellationToken = default) =>
        ControlAsync(new RevalueLootCommand(choice, NewCompletion()), cancellationToken);

    private async Task ExecuteRevalueLootAsync(ActivityLootEmv choice, CancellationToken cancellationToken)
    {
        // Only pickups whose value differs from the chosen EMV (or the other one, when the item has no chosen EMV).
        Guid[] ids;
        await using (var read = contextFactory())
        {
            var loot = read.ActivityEvents.AsNoTracking().Where(x => !x.IsRetracted &&
                (x.Kind == ActivityEventKind.Loot || x.Kind == ActivityEventKind.Trashed && x.LootSourceKind == "Loot"));
            var royal = ActivityItemValuationService.RoyalEmvPath;
            var blackMarket = ActivityItemValuationService.BlackMarketEmvPath;
            ids = await (choice == ActivityLootEmv.Royal
                    ? loot.Where(x => (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, royal) ?? ActivityDatabaseFunctions.JsonLong(x.DetailsJson, blackMarket)) != null &&
                        x.EstimatedUnitValue != (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, royal) ?? ActivityDatabaseFunctions.JsonLong(x.DetailsJson, blackMarket)))
                    : loot.Where(x => (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, blackMarket) ?? ActivityDatabaseFunctions.JsonLong(x.DetailsJson, royal)) != null &&
                        x.EstimatedUnitValue != (ActivityDatabaseFunctions.JsonLong(x.DetailsJson, blackMarket) ?? ActivityDatabaseFunctions.JsonLong(x.DetailsJson, royal))))
                .Select(x => x.Id).ToArrayAsync(cancellationToken);
        }
        if (ids.Length == 0) return;

        var revalued = 0;
        foreach (var chunk in ids.Chunk(250))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var context = contextFactory();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var sessions = new Dictionary<Guid, ActivitySession>();
            var encounters = new Dictionary<Guid, ActivityEncounter>();
            var rows = await context.ActivityEvents.Where(x => chunk.Contains(x.Id)).ToArrayAsync(cancellationToken);
            foreach (var row in rows)
            {
                if (row.IsRetracted || ActivityItemValuationService.LootEmvsOf(row) is not { } emvs || emvs.Pick(choice) is not { } pick) continue;
                if (row.EstimatedUnitValue == pick.Price && ActivityItemValuationService.IsBlackMarketEmv(row) == pick.BlackMarket) continue;
                if (row.Quantity > 0 && pick.Price > long.MaxValue / row.Quantity) continue;
                // The price keeps its time and the row its revision, so a later correction of the pickup still applies.
                var value = ActivityItemValuationService.WithPrice(row, pick.Price, row.PriceObservedAtUtc ?? DateTime.UtcNow,
                    ActivityItemValuationService.BasisQuality(row), pick.BlackMarket, emvs) with { PriceObservedAtUtc = row.PriceObservedAtUtc };
                await ApplyContributionAsync(context, row, -1, sessions, encounters, cancellationToken);
                await ApplyContributionAsync(context, value, 1, sessions, encounters, cancellationToken);
                context.Entry(row).CurrentValues.SetValues(value);
                revalued++;
            }
            var changes = new CommittedChanges();
            changes.Include(context);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            NotifyCommitted(changes);
        }
        Log.Information("Valued {Count} loot pickups at the {Choice} EMV.", revalued, choice == ActivityLootEmv.Royal ? "Royal" : "Black Market");
    }

    private sealed record RevalueLootCommand(ActivityLootEmv Choice, TaskCompletionSource Completion) : ControlCommand(Completion);
}
