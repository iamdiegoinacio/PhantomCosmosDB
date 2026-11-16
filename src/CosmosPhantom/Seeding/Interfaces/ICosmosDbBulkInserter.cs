using Microsoft.Azure.Cosmos;
using System.Threading;
using System.Threading.Tasks;
using CosmosPhantom.Configuration;

namespace CosmosPhantom.Seeding.Interfaces;

public interface ICosmosDbBulkInserter
{
    Task<int> BulkInsertAsync(Container container, ContainerConfig containerConfig, string jsonContent, CancellationToken ct);
}
