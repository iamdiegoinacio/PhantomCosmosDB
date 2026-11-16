#nullable enable
using Microsoft.Azure.Cosmos;
using Phantom.CosmosDB.Core.Application.Contracts;
using SdkResponseFactory = Phantom.CosmosDB.Core.Infrastructure.SdkInterop.CosmosResponseFactory;

namespace Phantom.CosmosDB.Core.Infrastructure.FeedIterators;

/// <summary>
/// Infrastructure adapter implementing <see cref="IFeedIteratorFactory"/>.
/// </summary>
internal sealed class InMemoryFeedIteratorFactory : IFeedIteratorFactory
{
    public FeedIterator<T> CreateTyped<T>(
        IReadOnlyList<T> items,
        int? maxItemCount = null,
        int initialOffset = 0,
        bool populateIndexMetrics = false,
        bool guaranteeFirstPage = false) =>
        new InMemoryFeedIterator<T>(items, maxItemCount, initialOffset)
        {
            PopulateIndexMetrics = populateIndexMetrics,
            GuaranteeFirstPage = guaranteeFirstPage
        };

    public FeedIterator<T> CreateLazy<T>(
        Func<IReadOnlyList<T>> factory,
        int? maxItemCount = null) =>
        new InMemoryFeedIterator<T>(factory, maxItemCount);

    public FeedIterator CreateStream(
        Func<IReadOnlyList<object>> itemsFactory,
        string wrapperProperty,
        Func<string>? sessionTokenFactory = null) =>
        new InMemoryStreamFeedIterator(itemsFactory, wrapperProperty, sessionTokenFactory);

    public FeedIterator CreateStreamFromFactory(
        Func<List<string>> itemsFactory,
        Func<string> sessionTokenProvider,
        int initialOffset,
        int? maxItemCount) =>
        SdkResponseFactory.CreateStreamFeedIteratorFromFactory(
            itemsFactory,
            sessionTokenProvider,
            initialOffset,
            maxItemCount);

    public FeedIterator CreateQueryStream(
        List<string> items,
        Func<string> sessionTokenProvider,
        int initialOffset = 0,
        int? maxItemCount = null) =>
        SdkResponseFactory.CreateStreamFeedIterator(
            items,
            sessionTokenProvider,
            initialOffset,
            maxItemCount);
}
