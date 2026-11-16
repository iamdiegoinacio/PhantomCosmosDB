#nullable enable
using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Infrastructure.FeedIterators;

/// <summary>
/// In-memory implementation of <see cref="FeedResponse{T}"/> for a single page of items.
/// </summary>
internal sealed class InMemoryFeedResponse<TItem> : FeedResponse<TItem>
{
    private readonly IReadOnlyList<TItem> _items;
    private readonly bool _populateIndexMetrics;
    private readonly string? _customIndexMetrics;

    public InMemoryFeedResponse(
        IReadOnlyList<TItem> items,
        string? continuationToken = null,
        bool populateIndexMetrics = false,
        string? customIndexMetrics = null,
        double requestCharge = 1.0)
    {
        _items = items;
        _populateIndexMetrics = populateIndexMetrics;
        _customIndexMetrics = customIndexMetrics;
        RequestCharge = requestCharge;
        ContinuationToken = continuationToken!;
        ActivityId = Guid.NewGuid().ToString();
        var headers = new Headers
        {
            ["x-ms-activity-id"] = ActivityId,
            ["x-ms-request-charge"] = RequestCharge.ToString(CultureInfo.InvariantCulture),
            ["x-ms-item-count"] = items.Count.ToString()
        };
        if (continuationToken is not null)
        {
            headers["x-ms-continuation"] = continuationToken;
        }
        Headers = headers;
    }

    public override Headers Headers { get; }
    public override IEnumerable<TItem> Resource => _items;
    public override HttpStatusCode StatusCode => HttpStatusCode.OK;
    public override CosmosDiagnostics Diagnostics => InMemoryFeedDiagnostics.Instance;
    public override int Count => _items.Count;
    public override string IndexMetrics => _populateIndexMetrics
        ? (_customIndexMetrics ?? "{\"UtilizedSingleIndexes\":[],\"PotentialSingleIndexes\":[],\"UtilizedCompositeIndexes\":[],\"PotentialCompositeIndexes\":[]}")
        : null!;
    public override string ContinuationToken { get; }
    public override double RequestCharge { get; }
    public override string ActivityId { get; }
    public override string ETag => null!;

    public override IEnumerator<TItem> GetEnumerator() => _items.GetEnumerator();
}
