using System.Threading;
using System.Threading.Tasks;
using CosmosPhantom.Configuration;

namespace CosmosPhantom.Seeding.Interfaces;

public interface ICosmosDbSeederService
{
    Task SeedAsync(string seedsFolderPath, CancellationToken ct = default);
}
