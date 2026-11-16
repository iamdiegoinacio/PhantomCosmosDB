using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using Phantom.CosmosDB.Configuration;

namespace Phantom.CosmosDB.Seeding.Interfaces;

public interface ICosmosDbManager
{
    Task<Database> CreateDatabaseIfNotExistsAsync(string databaseName);
    Task<Container> CreateContainerIfNotExistsAsync(Database database, ContainerConfig containerConfig);
}
