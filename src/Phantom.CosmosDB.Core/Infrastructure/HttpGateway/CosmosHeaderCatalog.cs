#nullable enable

namespace Phantom.CosmosDB.Core.Infrastructure.HttpGateway;

/// <summary>
/// Catalog of Cosmos DB HTTP <c>x-ms-*</c> headers recognised by the Gateway handler.
/// </summary>
internal static class CosmosHeaderCatalog
{
    /// <summary>
    /// Set of <c>x-ms-*</c> request headers that the gateway handler recognises and processes.
    /// Any <c>x-ms-*</c> header not in this set is recorded in <see cref="FakeCosmosHandler.UnrecognisedHeaders"/>.
    /// </summary>
    public static readonly HashSet<string> KnownRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "A-IM",
        "x-ms-documentdb-partitionkey",
        "x-ms-documentdb-isquery",
        "x-ms-documentdb-is-upsert",
        "x-ms-cosmos-is-batch-request",
        "x-ms-cosmos-is-query-plan-request",
        "x-ms-max-item-count",
        "x-ms-continuation",
        "x-ms-documentdb-partitionkeyrangeid",
        "x-ms-date",
        "x-ms-version",
        "x-ms-documentdb-query-enablecrosspartition",
        "x-ms-documentdb-query-iscontinuationexpected",
        "x-ms-documentdb-query-parallelizecrosspartitionquery",
        "x-ms-documentdb-populatequerymetrics",
        "x-ms-cosmos-correlated-activityid",
        "x-ms-activity-id",
        "x-ms-cosmos-sdk-supportedcapabilities",
        "x-ms-session-token",
        "x-ms-consistency-level",
        "x-ms-request-charge",
        "x-ms-cosmos-internal-is-query",
        "x-ms-cosmos-query-version",
        "x-ms-documentdb-collection-rid",
        "x-ms-cosmos-batch-ordered-response",
        "x-ms-cosmos-batch-atomic",
        "x-ms-cosmos-batch-continue-on-error",
        "x-ms-cosmos-sdk-version",
        "x-ms-cosmos-intended-collection-rid",
        "x-ms-cosmos-priority-level",
        "x-ms-cosmos-allow-tentative-writes",
        "x-ms-cosmos-physical-partition-count",
        "x-ms-documentdb-responsecontinuationtokenlimitinkb",
        "x-ms-documentdb-content-serialization-format",
        "x-ms-cosmos-query-optimisticdirectexecute",
        "x-ms-cosmos-supported-serialization-formats",
        "x-ms-cosmos-supported-query-features",
        "x-ms-documentdb-pre-trigger-include",
        "x-ms-documentdb-post-trigger-include",
        "x-ms-documentdb-script-enable-logging",
        "x-ms-offer-throughput",
        "If-Modified-Since",
    };
}
