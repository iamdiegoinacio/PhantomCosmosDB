using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cosmos.Phantom.SDK.Configuration;

/// <summary>
/// Resolve caminhos de configuração (<c>CosmosPhantom.config.json</c>) e da pasta <c>Resources</c>
/// a partir do projeto Host consumidor, do diretório de saída e da raiz da solução (.sln/.slnx/.git).
/// </summary>
internal static class PhantomResourcePathResolver
{
    public const string RootConfigFileName = "CosmosPhantom.config.json";
    public const string ResourcesFolderName = "Resources";
    public const string SeedsFolderName = "Seeds";
    private const int MaxAncestorDepth = 8;

    public static string? ResolveRootConfigFile(string? contentRootPath, string fileName = RootConfigFileName)
    {
        var roots = GetCandidateRoots(contentRootPath);

        foreach (var root in roots)
        {
            var directPath = Path.Combine(root, fileName);
            if (File.Exists(directPath))
                return directPath;
        }

        foreach (var root in roots)
        {
            var inResourcesPath = Path.Combine(root, ResourcesFolderName, fileName);
            if (File.Exists(inResourcesPath))
                return inResourcesPath;
        }

        return null;
    }

    public static string? ResolveResourceFile(string? contentRootPath, params string[] relativeSegments)
    {
        if (relativeSegments.Length == 0)
            return null;

        var relativePath = Path.Combine(relativeSegments);
        foreach (var root in GetCandidateRoots(contentRootPath))
        {
            var candidate = Path.Combine(root, ResourcesFolderName, relativePath);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string? ResolveSeedFile(string? explicitSeedsFolder, string? contentRootPath, string containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            return null;

        var fileName = $"{containerName}.json";

        if (!string.IsNullOrWhiteSpace(explicitSeedsFolder))
        {
            var explicitCandidate = Path.Combine(explicitSeedsFolder, fileName);
            if (File.Exists(explicitCandidate))
                return explicitCandidate;
        }

        var inResources = ResolveResourceFile(contentRootPath, SeedsFolderName, fileName);
        if (inResources != null)
            return inResources;

        // Compatibilidade legada: {ContentRootPath}/Seeds/{Container}.json
        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            var legacyCandidate = Path.Combine(contentRootPath, SeedsFolderName, fileName);
            if (File.Exists(legacyCandidate))
                return legacyCandidate;
        }

        return null;
    }

    public static string ResolvePrimarySeedsFolder(string? contentRootPath)
    {
        foreach (var root in GetCandidateRoots(contentRootPath))
        {
            var resourcesSeeds = Path.Combine(root, ResourcesFolderName, SeedsFolderName);
            if (Directory.Exists(resourcesSeeds))
                return resourcesSeeds;
        }

        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            var legacySeeds = Path.Combine(contentRootPath, SeedsFolderName);
            if (Directory.Exists(legacySeeds))
                return legacySeeds;

            return Path.Combine(contentRootPath, ResourcesFolderName, SeedsFolderName);
        }

        return Path.Combine(AppContext.BaseDirectory, ResourcesFolderName, SeedsFolderName);
    }

    internal static IReadOnlyList<string> GetCandidateRoots(string? contentRootPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();

        void AddIfNew(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            var full = Path.GetFullPath(dir);
            if (Directory.Exists(full) && seen.Add(full))
                roots.Add(full);
        }

        AddIfNew(contentRootPath);
        AddIfNew(AppContext.BaseDirectory);

        CollectAncestors(contentRootPath, AddIfNew);
        CollectAncestors(AppContext.BaseDirectory, AddIfNew);

        return roots;
    }

    private static void CollectAncestors(string? startPath, Action<string?> add)
    {
        if (string.IsNullOrWhiteSpace(startPath) || !Directory.Exists(startPath))
            return;

        var current = new DirectoryInfo(startPath);
        for (var depth = 0; depth < MaxAncestorDepth && current != null; depth++, current = current.Parent)
        {
            add(current.FullName);

            if (IsSolutionOrRepoRoot(current))
                break;
        }
    }

    private static bool IsSolutionOrRepoRoot(DirectoryInfo dir)
    {
        try
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")))
                return true;

            return dir.EnumerateFiles("*.sln").Any() || dir.EnumerateFiles("*.slnx").Any();
        }
        catch
        {
            return false;
        }
    }
}
