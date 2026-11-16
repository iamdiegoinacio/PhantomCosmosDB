using Microsoft.Azure.Cosmos;
using System.Threading.Tasks;
using CosmosPhantom.Configuration;

namespace CosmosPhantom.Seeding.Interfaces;

public interface ICosmosDbManager
{
    Task<Database> CreateDatabaseIfNotExistsAsync(string databaseName);
    Task<Container> CreateContainerIfNotExistsAsync(Database database, ContainerConfig containerConfig);
}
