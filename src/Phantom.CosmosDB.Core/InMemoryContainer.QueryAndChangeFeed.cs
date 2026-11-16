#nullable enable
using Phantom.CosmosDB.Core.Application.ChangeFeed;
using Phantom.CosmosDB.Core.Application.Query;
using Phantom.CosmosDB.Core.Infrastructure.SdkInterop;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using NSubstitute;

namespace Phantom.CosmosDB.Core;

internal partial class InMemoryContainer
{
    // ─── Query — Typed & Stream FeedIterators ─────────────────────────────────

    public override FeedIterator<T> GetItemQueryIterator<T>(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
        => ContainerQueryIteratorFactory.CreateTypedIterator<T>(
            queryDefinition, continuationToken, requestOptions, FilterItemsByQuery, JsonSettings);

    public override FeedIterator<T> GetItemQueryIterator<T>(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
        => ContainerQueryIteratorFactory.CreateTypedIterator<T>(
            queryText, continuationToken, requestOptions, GetAllItemsForPartition, FilterItemsByQuery, JsonSettings);

    public override FeedIterator<T> GetItemQueryIterator<T>(
        FeedRange feedRange,
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
        => ContainerQueryIteratorFactory.CreateTypedFeedRangeIterator<T>(
            feedRange, queryDefinition, continuationToken, requestOptions,
            PartitionKeyPaths, GetAllItemsForPartition, FilterItemsByQuery, JsonSettings);

    public override FeedIterator GetItemQueryStreamIterator(
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
        => ContainerQueryIteratorFactory.CreateStreamIterator(
            queryDefinition, continuationToken, requestOptions, FilterItemsByQuery, () => CurrentSessionToken);

    public override FeedIterator GetItemQueryStreamIterator(
        string? queryText = null,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
        => ContainerQueryIteratorFactory.CreateStreamIterator(
            queryText, continuationToken, requestOptions, GetAllItemsForPartition, FilterItemsByQuery, () => CurrentSessionToken);

    public override FeedIterator GetItemQueryStreamIterator(
        FeedRange feedRange,
        QueryDefinition queryDefinition,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null)
        => ContainerQueryIteratorFactory.CreateStreamFeedRangeIterator(
            feedRange, queryDefinition, continuationToken, requestOptions,
            PartitionKeyPaths, GetAllItemsForPartition, FilterItemsByQuery, JsonSettings, () => CurrentSessionToken);

    // ─── LINQ & TransactionalBatch ────────────────────────────────────────────

    public override IOrderedQueryable<T> GetItemLinqQueryable<T>(
        bool allowSynchronousQueryExecution = false,
        string? continuationToken = null,
        QueryRequestOptions? requestOptions = null,
        CosmosLinqSerializerOptions? linqSerializerOptions = null)
    {
        InMemoryFeedIteratorSetup.LastMaxItemCount = requestOptions?.MaxItemCount;
        return GetAllItemsForPartition(requestOptions)
            .Select(json => JsonConvert.DeserializeObject<T>(json, JsonSettings)!)
            .AsQueryable()
            .OrderBy(item => 0);
    }

    public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey)
        => new InMemoryTransactionalBatch(this, partitionKey);

    // ─── Change Feed — Iterators & Processor Builders ─────────────────────────

    public override FeedIterator<T> GetChangeFeedIterator<T>(
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions = null)
        => ContainerChangeFeedIteratorFactory.CreateTypedIterator<T>(
            _changeFeedLog, changeFeedStartFrom, changeFeedMode, changeFeedRequestOptions, JsonSettings);

    public FeedIterator<T> GetChangeFeedIterator<T>(long checkpoint)
        => ContainerChangeFeedIteratorFactory.CreateCheckpointIterator<T>(
            _changeFeedLog, checkpoint, JsonSettings);

    public override FeedIterator GetChangeFeedStreamIterator(
        ChangeFeedStartFrom changeFeedStartFrom,
        ChangeFeedMode changeFeedMode,
        ChangeFeedRequestOptions? changeFeedRequestOptions = null)
        => ContainerChangeFeedIteratorFactory.CreateStreamIterator(
            _changeFeedLog, changeFeedStartFrom, changeFeedMode, changeFeedRequestOptions, () => CurrentSessionToken);

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(
        string processorName, Container.ChangesHandler<T> onChangesDelegate)
        => ChangeFeedProcessorBuilderFactory.Create(processorName,
            new InMemoryChangeFeedProcessor<T>(this, onChangesDelegate));

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(
        string processorName, Container.ChangeFeedHandler<T> onChangesDelegate)
        => ChangeFeedProcessorBuilderFactory.Create(processorName,
            new InMemoryChangeFeedProcessor<T>(this, onChangesDelegate));

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(
        string processorName, Container.ChangeFeedStreamHandler onChangesDelegate)
        => ChangeFeedProcessorBuilderFactory.Create(processorName,
            new InMemoryChangeFeedStreamProcessor(this, onChangesDelegate));

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(
        string processorName, Container.ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate)
        => ChangeFeedProcessorBuilderFactory.Create(processorName,
            new InMemoryManualCheckpointChangeFeedProcessor<T>(this, onChangesDelegate));

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(
        string processorName, Container.ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate)
        => ChangeFeedProcessorBuilderFactory.Create(processorName,
            new InMemoryManualCheckpointStreamChangeFeedProcessor(this, onChangesDelegate));

    public override ChangeFeedEstimator GetChangeFeedEstimator(
        string processorName, Container leaseContainer)
        => Substitute.For<ChangeFeedEstimator>();

    public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(
        string processorName,
        Container.ChangesEstimationHandler estimationDelegate,
        TimeSpan? estimationPeriod = null)
        => ChangeFeedProcessorBuilderFactory.Create(processorName, new NoOpChangeFeedProcessor());

    public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(FeedRangeCalculator.GetFeedRanges(FeedRangeCount));
}
