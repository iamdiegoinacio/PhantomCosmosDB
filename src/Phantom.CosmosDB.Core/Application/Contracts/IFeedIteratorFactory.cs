#nullable enable
using Microsoft.Azure.Cosmos;

namespace Phantom.CosmosDB.Core.Application.Contracts;

/// <summary>
/// Port for creating typed and stream Cosmos DB SDK <see cref="FeedIterator"/> instances.
/// </summary>
internal interface IFeedIteratorFactory
{
    FeedIterator<T> CreateTyped<T>(
        IReadOnlyList<T> items,
        int? maxItemCount = null,
        int initialOffset = 0,
        bool populateIndexMetrics = false,
        bool guaranteeFirstPage = false);

    FeedIterator<T> CreateLazy<T>(
        Func<IReadOnlyList<T>> factory,
        int? maxItemCount = null);

    FeedIterator CreateStream(
        Func<IReadOnlyList<object>> itemsFactory,
        string wrapperProperty,
        Func<string>? sessionTokenFactory = null);

    FeedIterator CreateStreamFromFactory(
        Func<List<string>> itemsFactory,
        Func<string> sessionTokenProvider,
        int initialOffset,
        int? maxItemCount);

    FeedIterator CreateQueryStream(
        List<string> items,
        Func<string> sessionTokenProvider,
        int initialOffset = 0,
        int? maxItemCount = null);
}
