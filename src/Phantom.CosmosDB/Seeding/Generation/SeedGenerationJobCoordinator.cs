using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Phantom.CosmosDB.Core;
using ContainerConfig = Phantom.CosmosDB.Configuration.ContainerConfig;

namespace Phantom.CosmosDB.Seeding.Generation;

internal sealed class SeedGenerationJobCoordinator
{
    private readonly ConcurrentDictionary<string, (SeedGenerationJobStatus Status, CancellationTokenSource Cts)> _jobs = new();

    public SeedGenerationJobStatus StartJob(
        InMemoryContainer container,
        ContainerConfig? containerConfig,
        string seedsFolderPath,
        SeedGenerationRequest request)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12];
        var status = new SeedGenerationJobStatus
        {
            JobId = jobId,
            ContainerName = container.Id,
            TotalCount = Math.Clamp(request.Count, 1, 2_000_000)
        };
        request.Count = status.TotalCount;

        var cts = new CancellationTokenSource();
        _jobs[jobId] = (status, cts);

        _ = Task.Run(() =>
            SeedGenerationService.ExecuteGenerationJobAsync(
                status, container, containerConfig, seedsFolderPath, request, cts.Token),
            CancellationToken.None);

        return status;
    }

    public bool TryGetStatus(string jobId, out SeedGenerationJobStatus? status)
    {
        if (_jobs.TryGetValue(jobId, out var entry))
        {
            status = entry.Status;
            return true;
        }

        status = null;
        return false;
    }

    public bool TryCancel(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var entry))
        {
            entry.Cts.Cancel();
            return true;
        }
        return false;
    }
}
