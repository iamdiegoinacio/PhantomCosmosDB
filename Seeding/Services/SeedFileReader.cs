using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cosmos.Phantom.SDK.Configuration;
using Cosmos.Phantom.SDK.Seeding.Interfaces;
using Microsoft.Extensions.Hosting;

namespace Cosmos.Phantom.SDK.Seeding.Services;

public class SeedFileReader(IHostEnvironment? environment = null) : ISeedFileReader
{
    public async Task<string?> ReadSeedFileAsync(string folderPath, string containerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            return null;

        // 1. Tenta ler do disco do consumidor (Resources/Seeds na raiz da solução/Host ou caminho legado)
        var resolvedPath = PhantomResourcePathResolver.ResolveSeedFile(
            folderPath,
            environment?.ContentRootPath,
            containerName);

        if (resolvedPath != null)
        {
            return await File.ReadAllTextAsync(resolvedPath, ct);
        }

        // 2. Fallback: Tenta ler o arquivo padrão embutido na DLL do SDK
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Cosmos.Phantom.SDK.Resources.Seeds.{containerName}.json";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream != null)
        {
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }

        return null;
    }
}
