using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Phantom.CosmosDB.Dashboard.State;

internal sealed class CosmosPhantomTelemetryLog
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<CosmosPhantomLogEntry> _entries = new();
    private long _sequence;

    public void Add(
        string method,
        string path,
        string? containerName,
        int statusCode,
        int subStatusCode,
        double requestCharge,
        double durationMs,
        string? sqlQuery,
        bool isChaosInjected)
    {
        var seq = Interlocked.Increment(ref _sequence);
        _entries.Enqueue(new CosmosPhantomLogEntry(
            seq,
            DateTimeOffset.UtcNow,
            method,
            path,
            containerName,
            statusCode,
            subStatusCode,
            Math.Round(requestCharge, 2),
            Math.Round(durationMs, 2),
            sqlQuery,
            isChaosInjected));

        while (_entries.Count > MaxEntries)
        {
            _entries.TryDequeue(out _);
        }
    }

    public IReadOnlyList<CosmosPhantomLogEntry> GetRecent(int take = 200)
    {
        return _entries
            .Reverse()
            .Take(Math.Clamp(take, 1, MaxEntries))
            .ToList();
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
