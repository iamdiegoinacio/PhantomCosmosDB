using System.Threading;
using System.Threading.Tasks;

namespace CosmosPhantom.Seeding.Interfaces;

public interface ISeedFileReader
{
    Task<string?> ReadSeedFileAsync(string folderPath, string containerName, CancellationToken ct = default);
}
