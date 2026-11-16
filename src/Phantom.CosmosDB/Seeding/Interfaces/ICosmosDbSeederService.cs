using System.Threading;
using System.Threading.Tasks;
using Phantom.CosmosDB.Configuration;

namespace Phantom.CosmosDB.Seeding.Interfaces;

public interface ICosmosDbSeederService
{
    Task SeedAsync(string seedsFolderPath, CancellationToken ct = default);
}
