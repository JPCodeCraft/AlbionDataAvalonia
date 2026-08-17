using System;
using System.Collections.Generic;
using System.Linq;

namespace AlbionDataAvalonia.Players;

public sealed record PlayerIdentitySnapshot(
    int? ServerId,
    long? ObjectId,
    Guid? UserGuid,
    string PlayerName,
    string AllianceName,
    string GuildName);

public sealed class PlayerIdentityService
{
    private const int MaxIdentityCount = 20_000;

    private readonly object sync = new();
    private readonly Dictionary<PlayerIdentityKey, PlayerIdentitySnapshot> identitiesByName = new();
    private readonly Dictionary<PlayerGuidKey, PlayerIdentityKey> namesByGuid = new();
    private readonly Dictionary<PlayerObjectKey, PlayerIdentityKey> namesByObjectId = new();
    private readonly Queue<PlayerIdentityKey> identityInsertionOrder = new();

    public event Action<PlayerIdentitySnapshot>? IdentityChanged;

    public void AddOrUpdate(
        int? serverId,
        long? objectId,
        Guid? userGuid,
        string? playerName,
        string? guildName,
        string? allianceName)
    {
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPlayerName))
        {
            return;
        }

        var normalizedObjectId = objectId is > 0 ? objectId : null;
        Guid? normalizedUserGuid = userGuid is { } guid && guid != Guid.Empty
            ? guid
            : null;
        var key = new PlayerIdentityKey(serverId, NormalizePlayerKey(normalizedPlayerName));
        PlayerIdentitySnapshot? updatedIdentity = null;

        lock (sync)
        {
            var existingIdentity = FindExistingIdentityCore(
                key,
                serverId,
                normalizedObjectId,
                normalizedUserGuid);
            var identity = new PlayerIdentitySnapshot(
                serverId,
                normalizedObjectId ?? existingIdentity?.ObjectId,
                normalizedUserGuid ?? existingIdentity?.UserGuid,
                normalizedPlayerName,
                allianceName is null ? existingIdentity?.AllianceName ?? string.Empty : allianceName.Trim(),
                guildName is null ? existingIdentity?.GuildName ?? string.Empty : guildName.Trim());

            var isNewIdentity = !identitiesByName.TryGetValue(key, out var currentIdentity);
            if (!isNewIdentity && currentIdentity == identity)
            {
                return;
            }

            if (currentIdentity is not null)
            {
                RemoveIndexesCore(key, currentIdentity);
            }

            identitiesByName[key] = identity;
            if (isNewIdentity)
            {
                identityInsertionOrder.Enqueue(key);
            }

            if (identity.UserGuid is { } identityGuid)
            {
                namesByGuid[new PlayerGuidKey(serverId, identityGuid)] = key;
            }

            if (identity.ObjectId is { } identityObjectId)
            {
                namesByObjectId[new PlayerObjectKey(serverId, identityObjectId)] = key;
            }

            EvictOldestIdentitiesCore();
            updatedIdentity = identity;
        }

        IdentityChanged?.Invoke(updatedIdentity!);
    }

    public bool TryGetByName(int? serverId, string? playerName, out PlayerIdentitySnapshot identity)
    {
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedPlayerName))
        {
            identity = null!;
            return false;
        }

        var key = new PlayerIdentityKey(serverId, NormalizePlayerKey(normalizedPlayerName));
        lock (sync)
        {
            if (identitiesByName.TryGetValue(key, out identity!))
            {
                return true;
            }

            var candidates = identitiesByName
                .Where(entry => string.Equals(
                    entry.Value.PlayerName,
                    normalizedPlayerName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Value)
                .Take(2)
                .ToArray();
            if (candidates.Length == 1)
            {
                identity = candidates[0];
                return true;
            }
        }

        identity = null!;
        return false;
    }

    private PlayerIdentitySnapshot? FindExistingIdentityCore(
        PlayerIdentityKey key,
        int? serverId,
        long? objectId,
        Guid? userGuid)
    {
        if (identitiesByName.TryGetValue(key, out var identity))
        {
            return identity;
        }

        if (userGuid is { } guid
            && namesByGuid.TryGetValue(new PlayerGuidKey(serverId, guid), out var guidKey)
            && identitiesByName.TryGetValue(guidKey, out identity))
        {
            return identity;
        }

        if (objectId is { } id
            && namesByObjectId.TryGetValue(new PlayerObjectKey(serverId, id), out var objectKey)
            && identitiesByName.TryGetValue(objectKey, out identity))
        {
            return identity;
        }

        return null;
    }

    private void EvictOldestIdentitiesCore()
    {
        while (identitiesByName.Count > MaxIdentityCount
            && identityInsertionOrder.TryDequeue(out var oldestKey))
        {
            if (!identitiesByName.Remove(oldestKey, out var evictedIdentity))
            {
                continue;
            }

            RemoveIndexesCore(oldestKey, evictedIdentity);
        }
    }

    private void RemoveIndexesCore(PlayerIdentityKey key, PlayerIdentitySnapshot identity)
    {
        if (identity.UserGuid is { } identityGuid)
        {
            var guidKey = new PlayerGuidKey(identity.ServerId, identityGuid);
            if (namesByGuid.TryGetValue(guidKey, out var mappedKey) && mappedKey == key)
            {
                namesByGuid.Remove(guidKey);
            }
        }

        if (identity.ObjectId is { } identityObjectId)
        {
            var objectKey = new PlayerObjectKey(identity.ServerId, identityObjectId);
            if (namesByObjectId.TryGetValue(objectKey, out var mappedKey) && mappedKey == key)
            {
                namesByObjectId.Remove(objectKey);
            }
        }
    }

    private static string NormalizePlayerKey(string playerName)
    {
        return playerName.ToUpperInvariant();
    }

    private readonly record struct PlayerIdentityKey(int? ServerId, string NormalizedPlayerName);
    private readonly record struct PlayerGuidKey(int? ServerId, Guid UserGuid);
    private readonly record struct PlayerObjectKey(int? ServerId, long ObjectId);
}
