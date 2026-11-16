using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using CosmosPhantom.ChaosEngineering;

namespace CosmosPhantom.Configuration;

internal static class CosmosChaosConfigResolver
{
    private const string ChaosSettingsFileName = "Chaos.Settings.json";

    /// <summary>
    /// Lê a configuração de Chaos a partir de <c>CosmosPhantom.config.json</c>, <c>Resources/Chaos.Settings.json</c>
    /// ou <see cref="IConfiguration"/>. Por padrão nenhuma simulação de erro é habilitada.
    /// </summary>
    public static ChaosConfig Resolve(IConfiguration configuration, string? contentRootPath = null)
    {
        var chaosConfig =
            LoadFromRootConfig(contentRootPath)
            ?? LoadFromResourcesSettings(contentRootPath)
            ?? configuration.GetSection("CosmosChaosEngineering").Get<ChaosConfig>()
            ?? configuration.GetSection("CosmosDbEmulator:Chaos").Get<ChaosConfig>();

        return chaosConfig ?? new ChaosConfig();
    }

    private static ChaosConfig? LoadFromRootConfig(string? contentRootPath)
    {
        var rootJson = PhantomRootConfigResolver.LoadRootConfigJson(contentRootPath);
        if (rootJson == null) return null;

        var section = rootJson["CosmosChaosEngineering"]
                      ?? rootJson["Chaos"]
                      ?? rootJson["CosmosDbEmulator"]?["Chaos"];
        return section?.ToObject<ChaosConfig>();
    }

    private static ChaosConfig? LoadFromResourcesSettings(string? contentRootPath)
    {
        var path = PhantomResourcePathResolver.ResolveResourceFile(contentRootPath, ChaosSettingsFileName);
        if (path == null) return null;

        try
        {
            var jObj = JObject.Parse(File.ReadAllText(path));
            var section = jObj["CosmosChaosEngineering"] ?? jObj["Chaos"];
            return section?.ToObject<ChaosConfig>();
        }
        catch
        {
            return null;
        }
    }
}
