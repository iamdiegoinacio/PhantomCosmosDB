using Microsoft.Azure.Cosmos;
using System.Threading;
using System.Threading.Tasks;
using Phantom.CosmosDB.Configuration;

namespace Phantom.CosmosDB.Seeding.Interfaces;

public interface ICosmosDbBulkInserter
{
    Task<int> BulkInsertAsync(Container container, ContainerConfig containerConfig, string jsonContent, CancellationToken ct);
}
