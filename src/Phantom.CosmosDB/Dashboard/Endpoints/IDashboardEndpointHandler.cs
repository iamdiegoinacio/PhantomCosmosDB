using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Phantom.CosmosDB.Dashboard.State;
using Phantom.CosmosDB.Seeding.Interfaces;

namespace Phantom.CosmosDB.Dashboard.Endpoints;

internal sealed record DashboardRequestContext(
    HttpContext HttpContext,
    string Path,
    string Method,
    string RoutePrefix,
    CosmosPhantomRuntimeRegistry Registry,
    IHostEnvironment Environment,
    ICosmosDbSeederService? SeederService);

internal interface IDashboardEndpointHandler
{
    Task<bool> TryHandleAsync(DashboardRequestContext ctx);
}
