using System.Threading;

namespace Phantom.CosmosDB.Seeding;

/// <summary>
/// Garante execução única e idempotente do auto-seeding entre o <see cref="CosmosPhantomSeedingHostedService"/>
/// e chamadas manuais legadas a <see cref="PhantomCosmosSeederExtensions.UseCosmosPhantomSeederAsync"/>.
/// </summary>
internal sealed class CosmosSeedingExecutionTracker
{
    private int _executed;

    public bool TryMarkExecuted() => Interlocked.Exchange(ref _executed, 1) == 0;
}
