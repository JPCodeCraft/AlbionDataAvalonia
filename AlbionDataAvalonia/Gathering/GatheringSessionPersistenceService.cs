using AlbionDataAvalonia.DB;
using AlbionDataAvalonia.Gathering.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AlbionDataAvalonia.Gathering;

public sealed class GatheringSessionPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event Action? CompletedSessionsChanged;

    public async Task<bool> SaveUnfinishedCheckpointAsync(GatheringSessionCheckpoint checkpoint)
    {
        try
        {
            await using var db = new LocalContext();
            await using var transaction = await db.Database.BeginTransactionAsync();

            var existing = await db.GatheringUnfinishedSessionCheckpoints.ToListAsync();
            var row = existing.FirstOrDefault(x => x.SessionId == checkpoint.SessionId);
            foreach (var stale in existing.Where(x => x.SessionId != checkpoint.SessionId))
            {
                db.GatheringUnfinishedSessionCheckpoints.Remove(stale);
            }

            var isNew = row is null;
            if (row is null)
            {
                row = new GatheringUnfinishedSessionCheckpoint
                {
                    Id = Guid.NewGuid(),
                    SessionId = checkpoint.SessionId
                };
                await db.GatheringUnfinishedSessionCheckpoints.AddAsync(row);
            }

            row.StartedAtUtc = checkpoint.StartedAtUtc;
            row.LastActivityAtUtc = checkpoint.LastActivityAtUtc;
            row.UpdatedAtUtc = checkpoint.UpdatedAtUtc;
            row.IsPaused = checkpoint.IsPaused;
            row.Source = checkpoint.Source;
            row.PayloadJson = JsonSerializer.Serialize(checkpoint.Payload, JsonOptions);

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            Log.Information(
                isNew
                    ? "Gathering session checkpoint created. SessionId={SessionId}"
                    : "Gathering session checkpoint updated. SessionId={SessionId}",
                checkpoint.SessionId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save gathering session checkpoint. SessionId={SessionId}", checkpoint.SessionId);
            return false;
        }
    }

    public async Task<GatheringSessionCheckpoint?> LoadUnfinishedCheckpointAsync()
    {
        try
        {
            await using var db = new LocalContext();
            var rows = await db.GatheringUnfinishedSessionCheckpoints
                .AsNoTracking()
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToListAsync();

            if (rows.Count == 0)
            {
                return null;
            }

            var row = rows[0];
            if (rows.Count > 1)
            {
                Log.Warning("Multiple unfinished gathering session checkpoints found; using latest and discarding stale rows.");
                await DeleteUnfinishedCheckpointAsync(row.SessionId, deleteOthers: true);
            }

            GatheringSessionCheckpointPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<GatheringSessionCheckpointPayload>(row.PayloadJson, JsonOptions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unfinished gathering session checkpoint payload was corrupted. SessionId={SessionId}", row.SessionId);
                await DeleteUnfinishedCheckpointAsync(row.SessionId);
                return null;
            }

            if (payload is null)
            {
                Log.Warning("Unfinished gathering session checkpoint payload was empty. SessionId={SessionId}", row.SessionId);
                await DeleteUnfinishedCheckpointAsync(row.SessionId);
                return null;
            }

            return new GatheringSessionCheckpoint(
                row.SessionId,
                EnsureUtc(row.StartedAtUtc),
                EnsureUtc(row.LastActivityAtUtc),
                EnsureUtc(row.UpdatedAtUtc),
                row.IsPaused,
                row.Source,
                payload);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore unfinished gathering session checkpoint.");
            return null;
        }
    }

    public async Task<bool> SaveCompletedSessionAsync(GatheringCompletedSessionSnapshot snapshot)
    {
        if (snapshot.TotalAmount <= 0)
        {
            Log.Information("Skipped saving empty completed gathering session. SessionId={SessionId}", snapshot.Id);
            return true;
        }

        try
        {
            await using var db = new LocalContext();
            if (await db.GatheringCompletedSessions.AnyAsync(x => x.Id == snapshot.Id))
            {
                Log.Information("Completed gathering session already exists; skipping duplicate save. SessionId={SessionId}", snapshot.Id);
                return true;
            }

            var entity = new GatheringCompletedSession
            {
                Id = snapshot.Id,
                StartedAtUtc = snapshot.StartedAtUtc,
                EndedAtUtc = snapshot.EndedAtUtc,
                LastActivityAtUtc = snapshot.LastActivityAtUtc,
                ActiveElapsedSeconds = (long)Math.Round(snapshot.ActiveElapsed.TotalSeconds),
                TotalAmount = snapshot.TotalAmount,
                TotalEstimatedMarketValue = snapshot.TotalEstimatedMarketValue,
                SilverPerHour = snapshot.SilverPerHour,
                Source = snapshot.Source,
                Items = snapshot.Items.Select(x => new GatheringCompletedSessionItem
                {
                    Id = Guid.NewGuid(),
                    SessionId = snapshot.Id,
                    ItemId = x.ItemId,
                    Quality = x.Quality,
                    ItemUniqueName = x.ItemUniqueName,
                    ItemName = x.ItemName,
                    Amount = x.Amount,
                    EstimatedMarketValue = x.EstimatedMarketValue,
                    TotalEstimatedMarketValue = x.TotalEstimatedMarketValue,
                    Source = x.Source
                }).ToList()
            };

            await db.GatheringCompletedSessions.AddAsync(entity);
            await db.SaveChangesAsync();

            Log.Information("Saved completed gathering session. SessionId={SessionId}", snapshot.Id);
            CompletedSessionsChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save completed gathering session. SessionId={SessionId}", snapshot.Id);
            return false;
        }
    }

    public async Task<bool> DeleteUnfinishedCheckpointAsync(Guid? sessionId = null, bool deleteOthers = false)
    {
        try
        {
            await using var db = new LocalContext();
            var query = db.GatheringUnfinishedSessionCheckpoints.AsQueryable();
            if (sessionId is not null)
            {
                query = deleteOthers
                    ? query.Where(x => x.SessionId != sessionId.Value)
                    : query.Where(x => x.SessionId == sessionId.Value);
            }

            var rows = await query.ToListAsync();
            if (rows.Count > 0)
            {
                db.GatheringUnfinishedSessionCheckpoints.RemoveRange(rows);
                await db.SaveChangesAsync();
            }

            Log.Information("Deleted unfinished gathering session checkpoint. SessionId={SessionId}", sessionId);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete unfinished gathering session checkpoint. SessionId={SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> DeleteCompletedSessionAsync(Guid sessionId)
    {
        try
        {
            await using var db = new LocalContext();
            var session = await db.GatheringCompletedSessions
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == sessionId);

            if (session is null)
            {
                Log.Information("Completed gathering session was already deleted. SessionId={SessionId}", sessionId);
                CompletedSessionsChanged?.Invoke();
                return true;
            }

            db.GatheringCompletedSessions.Remove(session);
            await db.SaveChangesAsync();

            Log.Information("Deleted completed gathering session. SessionId={SessionId}", sessionId);
            CompletedSessionsChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete completed gathering session. SessionId={SessionId}", sessionId);
            return false;
        }
    }

    public async Task<IReadOnlyList<GatheringCompletedSessionSummary>> GetCompletedSessionsAsync(int take = 100)
    {
        try
        {
            await using var db = new LocalContext();
            return await db.GatheringCompletedSessions
                .AsNoTracking()
                .OrderByDescending(x => x.EndedAtUtc)
                .Take(take)
                .Select(x => new GatheringCompletedSessionSummary(
                    x.Id,
                    x.StartedAtUtc,
                    x.EndedAtUtc,
                    x.LastActivityAtUtc,
                    TimeSpan.FromSeconds(x.ActiveElapsedSeconds),
                    x.TotalAmount,
                    x.TotalEstimatedMarketValue,
                    x.SilverPerHour,
                    x.Source))
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read completed gathering sessions.");
            return Array.Empty<GatheringCompletedSessionSummary>();
        }
    }

    public async Task<GatheringCompletedSessionDetails?> GetCompletedSessionDetailsAsync(Guid sessionId)
    {
        try
        {
            await using var db = new LocalContext();
            var session = await db.GatheringCompletedSessions
                .AsNoTracking()
                .Include(x => x.Items)
                .SingleOrDefaultAsync(x => x.Id == sessionId);

            if (session is null)
            {
                return null;
            }

            var summary = new GatheringCompletedSessionSummary(
                session.Id,
                session.StartedAtUtc,
                session.EndedAtUtc,
                session.LastActivityAtUtc,
                TimeSpan.FromSeconds(session.ActiveElapsedSeconds),
                session.TotalAmount,
                session.TotalEstimatedMarketValue,
                session.SilverPerHour,
                session.Source);

            var items = session.Items
                .OrderByDescending(x => x.TotalEstimatedMarketValue ?? 0)
                .ThenByDescending(x => x.Amount)
                .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new GatheringCompletedSessionItemSnapshot(
                    x.ItemId,
                    x.Quality,
                    x.ItemUniqueName,
                    x.ItemName,
                    x.Amount,
                    x.EstimatedMarketValue,
                    x.TotalEstimatedMarketValue,
                    x.Source))
                .ToArray();

            return new GatheringCompletedSessionDetails(summary, items);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to read completed gathering session details. SessionId={SessionId}", sessionId);
            return null;
        }
    }

    public GatheringCompletedSessionSnapshot BuildCompletedSnapshotFromCheckpoint(
        GatheringSessionCheckpoint checkpoint,
        DateTime endedAtUtc)
    {
        var pauseIntervals = checkpoint.Payload.PauseIntervals
            .Select(x => new RestoredPauseInterval(x.StartedAtUtc, x.EndedAtUtc))
            .ToArray();
        var activeElapsed = GetActiveElapsed(checkpoint.StartedAtUtc, endedAtUtc, pauseIntervals);

        var items = checkpoint.Payload.Items
            .Select(x => new GatheringCompletedSessionItemSnapshot(
                x.ItemId,
                x.Quality,
                x.ItemUniqueName,
                x.ItemName,
                x.Amount,
                x.EstimatedMarketValue,
                x.EstimatedMarketValue is null ? null : x.EstimatedMarketValue.Value * x.Amount,
                x.Source))
            .ToArray();

        var totalValue = items.Sum(x => x.TotalEstimatedMarketValue ?? 0);
        var silverPerHour = activeElapsed.TotalSeconds <= 0 || totalValue <= 0
            ? 0
            : (long)Math.Round(totalValue / activeElapsed.TotalHours);

        return new GatheringCompletedSessionSnapshot(
            checkpoint.SessionId,
            checkpoint.StartedAtUtc,
            endedAtUtc,
            checkpoint.LastActivityAtUtc,
            activeElapsed,
            items.Sum(x => x.Amount),
            totalValue,
            silverPerHour,
            checkpoint.Source,
            items);
    }

    private static TimeSpan GetActiveElapsed(
        DateTime startedAtUtc,
        DateTime nowUtc,
        IReadOnlyList<RestoredPauseInterval> pauseIntervals)
    {
        var elapsed = nowUtc - startedAtUtc;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        foreach (var interval in pauseIntervals)
        {
            var pauseEnd = interval.EndedAtUtc ?? nowUtc;
            if (pauseEnd <= interval.StartedAtUtc)
            {
                continue;
            }

            elapsed -= pauseEnd - interval.StartedAtUtc;
        }

        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private sealed record RestoredPauseInterval(DateTime StartedAtUtc, DateTime? EndedAtUtc);
}
