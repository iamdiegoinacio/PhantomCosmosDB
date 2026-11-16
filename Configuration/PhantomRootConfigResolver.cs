using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Cosmos.Phantom.SDK.Configuration;

/// <summary>
/// Lê o arquivo unificado <c>CosmosPhantom.config.json</c> (quando presente) e resolve a flag
/// <c>UseCosmosPhantom</c>, garantindo que o emulador fique desligado (<c>false</c>) por padrão.
/// </summary>
internal static class PhantomRootConfigResolver
{
    public static JObject? LoadRootConfigJson(string? contentRootPath)
    {
        var path = PhantomResourcePathResolver.ResolveRootConfigFile(contentRootPath);
        if (path == null)
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsCosmosPhantomEnabled(IConfiguration configuration, string? contentRootPath)
    {
        var rootJson = LoadRootConfigJson(contentRootPath);
        if (rootJson != null && rootJson.TryGetValue("UseCosmosPhantom", StringComparison.OrdinalIgnoreCase, out var token))
        {
            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();
        }

        // Fallback compatível com appsettings*.json caso UseCosmosPhantom esteja definido no Host.
        return configuration.GetValue<bool>("UseCosmosPhantom", false);
    }
}
