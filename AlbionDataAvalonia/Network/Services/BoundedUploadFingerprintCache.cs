using System;
using System.Collections.Generic;

namespace AlbionDataAvalonia.Network.Services;

internal sealed class BoundedUploadFingerprintCache<T> where T : notnull
{
    private readonly int maxEntries;
    private readonly HashSet<T> fingerprints = new();
    private readonly Queue<T> insertionOrder = new();
    private readonly object lockObject = new();

    public BoundedUploadFingerprintCache(int maxEntries)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Maximum entries must be greater than zero.");
        }

        this.maxEntries = maxEntries;
    }

    public bool Contains(T fingerprint)
    {
        lock (lockObject)
        {
            return fingerprints.Contains(fingerprint);
        }
    }

    public bool Add(T fingerprint)
    {
        lock (lockObject)
        {
            if (!fingerprints.Add(fingerprint))
            {
                return false;
            }

            insertionOrder.Enqueue(fingerprint);
            TrimToMaxEntries();
            return true;
        }
    }

    private void TrimToMaxEntries()
    {
        while (fingerprints.Count > maxEntries && insertionOrder.Count > 0)
        {
            var oldest = insertionOrder.Dequeue();
            fingerprints.Remove(oldest);
        }
    }
}
