using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Cosmos.Phantom.SDK.Configuration;

internal static class CosmosEmulatorConfigResolver
{
    private const string SettingsFileName = "Cosmos.Phantom.Settings.json";

    /// <summary>
    /// Lê a configuração do <c>CosmosPhantom.config.json</c> (raiz), de <c>Resources/Cosmos.Phantom.Settings.json</c>
    /// ou de <see cref="IConfiguration"/>, aplicando fallback/merge com a configuração padrão embutida no SDK.
    /// </summary>
    public static CosmosDbEmulatorConfig? Resolve(IConfiguration configuration, string? contentRootPath = null)
    {
        var emulatorConfig =
            LoadFromRootConfig(contentRootPath)
            ?? LoadFromResourcesSettings(contentRootPath)
            ?? configuration.GetSection("CosmosDbEmulator").Get<CosmosDbEmulatorConfig>();

        var fallbackConfig = LoadEmbeddedConfig();

        if (emulatorConfig == null)
        {
            emulatorConfig = fallbackConfig;
        }
        else if (fallbackConfig != null)
        {
            // Mescla as configurações caso o usuário tenha fornecido apenas algumas propriedades
            emulatorConfig.DatabaseName ??= fallbackConfig.DatabaseName;
            emulatorConfig.Containers ??= fallbackConfig.Containers;
        }

        return emulatorConfig;
    }

    private static CosmosDbEmulatorConfig? LoadFromRootConfig(string? contentRootPath)
    {
        var rootJson = PhantomRootConfigResolver.LoadRootConfigJson(contentRootPath);
        return rootJson?["CosmosDbEmulator"]?.ToObject<CosmosDbEmulatorConfig>();
    }

    private static CosmosDbEmulatorConfig? LoadFromResourcesSettings(string? contentRootPath)
    {
        var path = PhantomResourcePathResolver.ResolveResourceFile(contentRootPath, SettingsFileName);
        if (path == null) return null;

        try
        {
            var jObj = JObject.Parse(File.ReadAllText(path));
            return jObj["CosmosDbEmulator"]?.ToObject<CosmosDbEmulatorConfig>();
        }
        catch
        {
            return null;
        }
    }

    private static CosmosDbEmulatorConfig? LoadEmbeddedConfig()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Cosmos.Phantom.SDK.Resources.Cosmos.Phantom.Settings.json";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var jObj = JObject.Parse(json);
            
            return jObj["CosmosDbEmulator"]?.ToObject<CosmosDbEmulatorConfig>();
        }
        catch
        {
            return null; // Falha segura se o embedded resource não for encontrado
        }
    }
}
