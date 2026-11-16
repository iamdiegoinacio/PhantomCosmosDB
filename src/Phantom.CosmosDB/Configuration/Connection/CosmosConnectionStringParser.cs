using System;
using System.Collections.Generic;
using Phantom.CosmosDB.Exceptions;

namespace Phantom.CosmosDB.Configuration.Connection;

/// <summary>
/// Faz o parse de Connection Strings no formato padrão Azure Cosmos DB (<c>Key=Value;</c>)
/// e identifica se a conexão deve usar o emulador em memória (<see cref="CosmosConnectionMode.Phantom"/>)
/// ou o serviço real do Azure Cosmos DB (<see cref="CosmosConnectionMode.Real"/>).
/// </summary>
internal static class CosmosConnectionStringParser
{
    private static readonly HashSet<string> PhantomHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cosmosphantom.local",
        "phantom.local",
        "in-memory.cosmos.local"
    };

    private static readonly HashSet<string> NonAzureConvenienceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Database",
        "DatabaseName",
        "Container",
        "ContainerName",
        "Mode",
        "UsePhantom"
    };

    public static CosmosConnectionSpec Parse(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CosmosDbEmulatorConfigurationException(
                "A Connection String do Cosmos DB está ausente ou vazia. " +
                "Configure 'ConnectionStrings:CosmosDb' (ou 'CosmosDb:ConnectionString') no appsettings.");
        }

        var parts = ParseKeyValuePairs(connectionString);

        parts.TryGetValue("AccountEndpoint", out var endpoint);
        parts.TryGetValue("AccountKey", out var accountKey);

        var databaseName = GetFirstValue(parts, "Database", "DatabaseName");
        var containerName = GetFirstValue(parts, "Container", "ContainerName");

        var mode = DetectMode(connectionString, parts, endpoint);
        var sanitizedAzure = BuildSanitizedAzureConnectionString(parts);

        if (mode == CosmosConnectionMode.Real &&
            (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(accountKey)))
        {
            throw new CosmosDbEmulatorConfigurationException(
                "Connection String do Azure Cosmos DB inválida: 'AccountEndpoint' e 'AccountKey' são obrigatórios.");
        }

        return new CosmosConnectionSpec(
            mode,
            connectionString,
            sanitizedAzure,
            endpoint,
            accountKey,
            databaseName,
            containerName);
    }

    private static CosmosConnectionMode DetectMode(
        string raw,
        IReadOnlyDictionary<string, string> parts,
        string? endpoint)
    {
        if (raw.StartsWith("phantom://", StringComparison.OrdinalIgnoreCase))
            return CosmosConnectionMode.Phantom;

        if (parts.TryGetValue("Mode", out var modeValue) &&
            string.Equals(modeValue, "InMemory", StringComparison.OrdinalIgnoreCase))
            return CosmosConnectionMode.Phantom;

        if (parts.TryGetValue("UsePhantom", out var usePhantomValue) &&
            bool.TryParse(usePhantomValue, out var usePhantom) &&
            usePhantom)
            return CosmosConnectionMode.Phantom;

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            if (endpoint.StartsWith("phantom://", StringComparison.OrdinalIgnoreCase))
                return CosmosConnectionMode.Phantom;

            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
                PhantomHosts.Contains(uri.Host))
                return CosmosConnectionMode.Phantom;
        }

        return CosmosConnectionMode.Real;
    }

    private static Dictionary<string, string> ParseKeyValuePairs(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var segments = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var segment in segments)
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = segment[..separatorIndex].Trim();
            var value = segment[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrEmpty(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static string? GetFirstValue(IReadOnlyDictionary<string, string> parts, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (parts.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
                return val;
        }

        return null;
    }

    private static string BuildSanitizedAzureConnectionString(IReadOnlyDictionary<string, string> parts)
    {
        var azureSegments = new List<string>();
        foreach (var kvp in parts)
        {
            if (!NonAzureConvenienceKeys.Contains(kvp.Key))
            {
                azureSegments.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return string.Join(';', azureSegments) + (azureSegments.Count > 0 ? ";" : string.Empty);
    }
}
