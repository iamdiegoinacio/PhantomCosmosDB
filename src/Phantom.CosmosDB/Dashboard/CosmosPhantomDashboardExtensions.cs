using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Phantom.CosmosDB.Dashboard;
using Phantom.CosmosDB.Dashboard.State;

namespace Phantom.CosmosDB;

public static class CosmosPhantomDashboardExtensions
{
    /// <summary>
    /// Habilita o painel gráfico Web do CosmosPhantom (ex.: <c>/_phantom</c>) para inspecionar
    /// bancos/containers, executar queries SQL, ver logs ao vivo e controlar Chaos Engineering em runtime.
    /// É um no-op silencioso em <c>Production</c> ou quando conectado ao Azure Cosmos DB real.
    /// </summary>
    public static IApplicationBuilder UsePhantomDashboard(
        this IApplicationBuilder app,
        string routePrefix = "/_phantom")
    {
        ArgumentNullException.ThrowIfNull(app);

        var env = app.ApplicationServices.GetService<IHostEnvironment>();
        var registry = app.ApplicationServices.GetService<CosmosPhantomRuntimeRegistry>();

        // Guardrail de segurança: nunca expor em Production ou quando o modo Phantom não está ativo
        if (env == null || !env.IsDevelopment() || registry == null)
        {
            return app;
        }

        var normalizedPrefix = NormalizePrefix(routePrefix);
        return app.Map(normalizedPrefix, branch =>
        {
            branch.UseMiddleware<CosmosPhantomDashboardMiddleware>(normalizedPrefix);
        });
    }

    private static string NormalizePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix))
            return "/_phantom";

        var trimmed = "/" + routePrefix.Trim().Trim('/');
        return trimmed == "/" ? "/_phantom" : trimmed;
    }
}
