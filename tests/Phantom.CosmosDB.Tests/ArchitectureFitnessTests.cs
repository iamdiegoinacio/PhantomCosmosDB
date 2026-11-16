using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Phantom.CosmosDB.Tests;

/// <summary>
/// Evolutionary Architecture Fitness Functions verifying layer boundaries inside Phantom.CosmosDB.Core.
/// </summary>
public sealed class ArchitectureFitnessTests
{
    private static readonly Assembly CoreAssembly =
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Phantom.CosmosDB.Core")
        ?? Assembly.Load("Phantom.CosmosDB.Core");

    [Fact]
    public void DomainLayer_ShouldNotDependOnApplicationOrInfrastructure()
    {
        var domainTypes = CoreAssembly.GetTypes()
            .Where(t => t.Namespace is not null &&
                        t.Namespace.StartsWith("Phantom.CosmosDB.Core.Domain", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(domainTypes);

        foreach (var type in domainTypes)
        {
            var referencedTypes = GetDirectlyReferencedTypes(type);
            var violations = referencedTypes
                .Where(rt => rt.Namespace is not null &&
                             (rt.Namespace.StartsWith("Phantom.CosmosDB.Core.Application", StringComparison.Ordinal) ||
                              rt.Namespace.StartsWith("Phantom.CosmosDB.Core.Infrastructure", StringComparison.Ordinal)))
                .Select(rt => $"{type.FullName} -> {rt.FullName}")
                .ToList();

            Assert.True(violations.Count == 0, $"Domain dependency violation(s): {string.Join("; ", violations)}");
        }
    }

    private static Type[] GetDirectlyReferencedTypes(Type type)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return type.GetFields(Flags).Select(f => f.FieldType)
            .Concat(type.GetProperties(Flags).Select(p => p.PropertyType))
            .Concat(type.GetMethods(Flags).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(type.GetConstructors(Flags).SelectMany(c => c.GetParameters().Select(p => p.ParameterType)))
            .Distinct()
            .ToArray();
    }
}
