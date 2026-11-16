namespace Phantom.CosmosDB.Configuration.Connection;

internal sealed record CosmosConnectionSpec(
    CosmosConnectionMode Mode,
    string RawConnectionString,
    string SanitizedAzureConnectionString,
    string? AccountEndpoint,
    string? AccountKey,
    string? DatabaseName,
    string? ContainerName);
