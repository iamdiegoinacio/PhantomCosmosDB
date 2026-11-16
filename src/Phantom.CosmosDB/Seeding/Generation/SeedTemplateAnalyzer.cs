using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Phantom.CosmosDB.Configuration;

namespace Phantom.CosmosDB.Seeding.Generation;

internal sealed class SeedSchemaProfile
{
    public required JObject Template { get; init; }
    public Dictionary<string, List<string>> ObservedStringPools { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UniquePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class SeedTemplateAnalyzer
{
    public static SeedSchemaProfile BuildProfile(
        JObject template,
        IEnumerable<JObject> existingSamples,
        ContainerConfig? containerConfig)
    {
        var profile = new SeedSchemaProfile { Template = template };

        if (containerConfig?.UniqueKeys != null)
        {
            foreach (var uk in containerConfig.UniqueKeys)
            {
                foreach (var path in uk)
                {
                    profile.UniquePaths.Add(NormalizePath(path));
                }
            }
        }

        foreach (var sample in existingSamples.Take(200))
        {
            CollectStringValues(sample, string.Empty, profile.ObservedStringPools);
        }

        return profile;
    }

    public static JObject SynthesizeDocument(
        SeedSchemaProfile profile,
        string containerName,
        long sequenceIndex,
        SeedIdStrategy idStrategy,
        string? idPrefix,
        GuidV5InputMode guidV5Mode,
        string? guidV5Template,
        Guid? guidV5Namespace,
        Random random)
    {
        string? nameContext = null;
        var clone = (JObject)SynthesizeToken(profile.Template, string.Empty, profile, sequenceIndex, random, ref nameContext);

        var id = SeedIdGenerator.GenerateId(
            idStrategy,
            containerName,
            sequenceIndex,
            idPrefix,
            guidV5Mode,
            guidV5Template,
            guidV5Namespace,
            clone,
            random);

        clone["id"] = id;
        return clone;
    }

    private static JToken SynthesizeToken(
        JToken templateToken,
        string currentPath,
        SeedSchemaProfile profile,
        long sequenceIndex,
        Random random,
        ref string? nameContext)
    {
        switch (templateToken)
        {
            case JObject obj:
            {
                var result = new JObject();
                foreach (var prop in obj.Properties())
                {
                    if (prop.Name.StartsWith('_'))
                        continue;

                    var childPath = string.IsNullOrEmpty(currentPath) ? prop.Name : $"{currentPath}.{prop.Name}";
                    if (prop.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(currentPath))
                    {
                        result["id"] = string.Empty;
                        continue;
                    }

                    result[prop.Name] = SynthesizeToken(prop.Value, childPath, profile, sequenceIndex, random, ref nameContext);

                    if (prop.Name.Equals("nome", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        nameContext = result[prop.Name]?.ToString();
                    }
                }
                return result;
            }

            case JArray arr:
            {
                var result = new JArray();
                if (arr.Count > 0)
                {
                    var sample = arr[0];
                    result.Add(SynthesizeToken(sample, currentPath, profile, sequenceIndex, random, ref nameContext));
                }
                return result;
            }

            case JValue val:
                return SynthesizePrimitive(val, currentPath, profile, sequenceIndex, random, nameContext);

            default:
                return templateToken.DeepClone();
        }
    }

    private static JValue SynthesizePrimitive(
        JValue sample,
        string path,
        SeedSchemaProfile profile,
        long sequenceIndex,
        Random random,
        string? nameContext)
    {
        var leafName = path.Split('.').LastOrDefault() ?? "campo";

        if (profile.UniquePaths.Contains(path))
        {
            return sample.Type == JTokenType.Integer
                ? new JValue(sequenceIndex)
                : new JValue($"{leafName.ToUpperInvariant()}-{sequenceIndex:D6}");
        }

        switch (sample.Type)
        {
            case JTokenType.Integer:
            {
                var baseVal = sample.Value<long>();
                if (leafName.Equals("ano", StringComparison.OrdinalIgnoreCase) || leafName.Equals("year", StringComparison.OrdinalIgnoreCase))
                    return new JValue(random.Next(2020, 2027));
                var max = Math.Max(10, Math.Abs(baseVal) * 4);
                var min = baseVal < 0 ? -max : 1;
                return new JValue(random.Next((int)Math.Max(int.MinValue, min), (int)Math.Min(int.MaxValue, max + 1)));
            }

            case JTokenType.Float:
            {
                var baseVal = Math.Max(1.0, Math.Abs(sample.Value<double>()));
                var generated = Math.Round((0.2 + random.NextDouble() * 2.5) * baseVal, 2);
                return new JValue(generated);
            }

            case JTokenType.Boolean:
                return new JValue(random.Next(2) == 0);

            case JTokenType.String:
            {
                profile.ObservedStringPools.TryGetValue(path, out var pool);
                var str = RealisticDataFaker.NextStringForField(
                    leafName,
                    sample.Value<string>(),
                    pool,
                    sequenceIndex,
                    random,
                    nameContext);
                return new JValue(str);
            }

            default:
                return (JValue)sample.DeepClone();
        }
    }

    private static void CollectStringValues(
        JObject obj,
        string prefix,
        Dictionary<string, List<string>> pools)
    {
        foreach (var prop in obj.Properties())
        {
            if (prop.Name.StartsWith('_')) continue;
            var path = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

            if (prop.Value is JObject nested)
            {
                CollectStringValues(nested, path, pools);
            }
            else if (prop.Value.Type == JTokenType.String)
            {
                var val = prop.Value.Value<string>();
                if (string.IsNullOrWhiteSpace(val)) continue;
                if (!pools.TryGetValue(path, out var list))
                {
                    list = [];
                    pools[path] = list;
                }
                if (list.Count < 32 && !list.Contains(val))
                    list.Add(val);
            }
        }
    }

    private static string NormalizePath(string path) =>
        path.TrimStart('/').Replace('/', '.');
}
