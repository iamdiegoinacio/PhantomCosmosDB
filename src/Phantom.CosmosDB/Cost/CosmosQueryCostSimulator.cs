using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Phantom.CosmosDB.Configuration;
using Phantom.CosmosDB.Configuration.Cost;
using Phantom.CosmosDB.Core;

namespace Phantom.CosmosDB.Cost;

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public sealed class QueryCostSimulationRequest
{
    public string Region { get; set; } = CosmosRegionalPricingCatalog.DefaultRegionId;
    public string Sql { get; set; } = "SELECT TOP 100 c.id, c.total, c.status FROM c WHERE c.tenantId = @tenantId AND c.status = 'paid' ORDER BY c.createdAt DESC";
    public long TotalItemsInContainer { get; set; } = 100_000;
    public int ReturnedItemsCount { get; set; } = 100;
    public double AvgDocumentSizeKb { get; set; } = 1.0;
    public int PhysicalPartitions { get; set; } = 1;
    public List<string> PartitionKeyPaths { get; set; } = ["/tenantId"];
    public string IndexingMode { get; set; } = "Consistent";
    public List<List<EmulatorCompositePath>> CompositeIndexes { get; set; } = [];
    public long QueriesPerMonth { get; set; } = 1_000_000;
    public double StorageGb { get; set; } = 10.0;
    public int ProvisionedRuOverride { get; set; }
    public string ConsistencyLevel { get; set; } = "Session";
    public bool ApplyFreeTier { get; set; }
    public bool AvailabilityZones { get; set; }
    public int AdditionalRegionsCount { get; set; }
    public bool MultiRegionWrites { get; set; }
    public bool HasSpatialIndex { get; set; } = true;
    public bool IsGeohashPartitioned { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
public sealed class GeoFleetSimulationRequest
{
    public string Region { get; set; } = CosmosRegionalPricingCatalog.DefaultRegionId;
    public int Vehicles { get; set; } = 1000;
    public double IntervalSeconds { get; set; } = 10.0;
    public double RuPerWrite { get; set; } = 5.0;
    public int DaysInMonth { get; set; } = 30;
    public double PeakMultiplier { get; set; } = 1.5;
    public double QueriesPerSecond { get; set; } = 20.0;
    public double RuPerReadQuery { get; set; } = 4.0;
    public double AvgDocumentSizeKb { get; set; } = 1.0;
    public int RetentionDays { get; set; } = 1;
    public bool ApplyFreeTier { get; set; }
}

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed record GeoFleetSimulationResult(
    CosmosRegionPricing Region,
    CosmosRegionPricing BaselineRegion,
    double WritesPerSecond,
    double WritesPerMonth,
    double WriteRuPerSecond,
    double WriteRuPerMonth,
    double ReadRuPerSecond,
    double ReadRuPerMonth,
    double AverageTotalRuPerSecond,
    double PeakTotalRuPerSecond,
    double TotalMonthlyRus,
    double TotalMonthlyMillionRus,
    double EstimatedStorageGb,
    int RecommendedProvisionedRu,
    MonthlyCostEstimate Serverless,
    MonthlyCostEstimate Provisioned,
    MonthlyCostEstimate Autoscale,
    MonthlyCostEstimate BaselineServerless,
    IReadOnlyList<string> Warnings);

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed record QueryCostBreakdown(
    double BaseQueryRu,
    double DocumentReadRuUnit,
    double ResultScanRu,
    double CrossPartitionOverheadRu,
    double IndexScanPenaltyRu,
    double ConsistencyMultiplier,
    double TotalRuPerExecution,
    bool IsCrossPartition,
    bool IsFullScan);

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed record MonthlyCostEstimate(
    string Model,
    double ProcessingUsd,
    double StorageUsd,
    double TotalUsd,
    double? MaxTotalUsd,
    string Notes);

[JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
internal sealed record QueryCostSimulationResult(
    CosmosRegionPricing Region,
    CosmosRegionPricing BaselineRegion,
    QueryCostBreakdown RuBreakdown,
    double TotalMonthlyRus,
    double TotalMonthlyMillionRus,
    double AverageRuPerSecond,
    int RecommendedProvisionedRu,
    double BreakEvenMillionRusFor400Ru,
    double RegionalServerlessDiffPercentVsBaseline,
    double RegionalProvisionedDiffPercentVsBaseline,
    MonthlyCostEstimate Serverless,
    MonthlyCostEstimate Provisioned,
    MonthlyCostEstimate Autoscale,
    MonthlyCostEstimate BaselineServerless,
    MonthlyCostEstimate BaselineProvisioned,
    IReadOnlyList<string> Warnings);

internal static class CosmosQueryCostSimulator
{
    private static readonly (double SizeKb, double ReadRu)[] DocReadTable =
    [
        (1, 1.00),
        (4, 1.14),
        (16, 1.67),
        (64, 4.76),
        (128, 9.95),
        (256, 20.28),
        (1024, 145.90)
    ];

    public static QueryCostSimulationResult Simulate(QueryCostSimulationRequest request)
    {
        var region = CosmosRegionalPricingCatalog.GetOrDefault(request.Region);
        var baseline = CosmosRegionalPricingCatalog.GetOrDefault(CosmosRegionalPricingCatalog.BaselineRegionId);

        var breakdown = EstimateQueryRus(request, out var warnings);

        var queriesPerMonth = Math.Max(0, request.QueriesPerMonth);
        var totalMonthlyRus = breakdown.TotalRuPerExecution * queriesPerMonth;
        var totalMonthlyMillionRus = totalMonthlyRus / 1_000_000.0;
        var avgRuPerSec = totalMonthlyRus / (CosmosRegionalPricingCatalog.HoursPerMonth * 3600.0);

        var recommendedRu = request.ProvisionedRuOverride > 0
            ? Math.Max(400, (int)(Math.Ceiling(request.ProvisionedRuOverride / 100.0) * 100))
            : Math.Max(400, (int)(Math.Ceiling(avgRuPerSec / 100.0) * 100));

        var serverless = CalculateServerless(region, totalMonthlyMillionRus, request);
        var provisioned = CalculateProvisioned(region, recommendedRu, request);
        var autoscale = CalculateAutoscale(region, recommendedRu, request);

        var baselineServerless = CalculateServerless(baseline, totalMonthlyMillionRus, request);
        var baselineProvisioned = CalculateProvisioned(baseline, recommendedRu, request);

        // Break-even para 400 RU/s (referência Seção 6 do EstudoCustos.md)
        var prov400ProcessingOnly = 0.4 * region.Monthly1000RuProvisionedUsd;
        var breakEven400 = region.ServerlessPerMillionRuUsd > 0
            ? Math.Round(prov400ProcessingOnly / region.ServerlessPerMillionRuUsd, 2)
            : 0;

        var serverlessDiffPct = baselineServerless.ProcessingUsd > 0
            ? Math.Round(((serverless.ProcessingUsd - baselineServerless.ProcessingUsd) / baselineServerless.ProcessingUsd) * 100.0, 1)
            : 0;

        var provDiffPct = baselineProvisioned.ProcessingUsd > 0
            ? Math.Round(((provisioned.ProcessingUsd - baselineProvisioned.ProcessingUsd) / baselineProvisioned.ProcessingUsd) * 100.0, 1)
            : 0;

        return new QueryCostSimulationResult(
            region,
            baseline,
            breakdown,
            Math.Round(totalMonthlyRus, 2),
            Math.Round(totalMonthlyMillionRus, 4),
            Math.Round(avgRuPerSec, 2),
            recommendedRu,
            breakEven400,
            serverlessDiffPct,
            provDiffPct,
            serverless,
            provisioned,
            autoscale,
            baselineServerless,
            baselineProvisioned,
            warnings);
    }

    public static double EstimateServerlessUsd(double requestCharge, CosmosCostOptions costOptions)
    {
        if (requestCharge <= 0) return 0;
        var region = CosmosRegionalPricingCatalog.GetOrDefault(costOptions.Region);
        var az = costOptions.AvailabilityZones ? 1.25 : 1.0;
        return Math.Round((requestCharge / 1_000_000.0) * region.ServerlessPerMillionRuUsd * az, 8);
    }

    private static QueryCostBreakdown EstimateQueryRus(
        QueryCostSimulationRequest request,
        out List<string> warnings)
    {
        warnings = [];
        var sql = string.IsNullOrWhiteSpace(request.Sql) ? "SELECT * FROM c" : request.Sql.Trim();
        CosmosSqlParser.TryParse(sql, out var parsed);

        var docSizeKb = Math.Clamp(request.AvgDocumentSizeKb, 0.5, 2048);
        var docReadRu = InterpolateDocumentReadRu(docSizeKb);
        var totalItems = Math.Max(0, request.TotalItemsInContainer);
        var returnedItems = Math.Clamp(request.ReturnedItemsCount, 0, (int)Math.Min(int.MaxValue, Math.Max(totalItems, request.ReturnedItemsCount)));
        var physicalPartitions = Math.Max(1, request.PhysicalPartitions);

        var isFullScan = string.Equals(request.IndexingMode, "None", StringComparison.OrdinalIgnoreCase);
        var isCrossPartition = !FiltersByPartitionKey(sql, request.PartitionKeyPaths);

        double baseRu = 2.5;
        double resultScanRu;
        double crossPartitionRu = 0;
        double indexScanPenaltyRu = 0;

        if (isFullScan)
        {
            var itemsScanned = Math.Max(1, totalItems);
            resultScanRu = itemsScanned * docReadRu * 0.25;
            warnings.Add("IndexingMode=None exige scan completo da coleção (e falha com SubStatus 1004 sem EnableScanInQuery=true).");
        }
        else
        {
            var isSelectStar = sql.Contains("SELECT *", StringComparison.OrdinalIgnoreCase) ||
                               sql.Contains("SELECT TOP", StringComparison.OrdinalIgnoreCase) && sql.Contains(" * ", StringComparison.OrdinalIgnoreCase);
            var projectionFactor = isSelectStar ? 0.085 : 0.075;
            resultScanRu = returnedItems * docReadRu * projectionFactor;

            if (isSelectStar && docSizeKb >= 16)
            {
                warnings.Add("SELECT * em documentos grandes (>=16 KB) aumenta tráfego e RUs; projete apenas os campos necessários.");
            }
        }

        if (isCrossPartition && physicalPartitions > 1)
        {
            var effectivePartitions = request.IsGeohashPartitioned ? Math.Min(4, physicalPartitions) : physicalPartitions;
            crossPartitionRu = 2.5 * effectivePartitions;
            if (request.IsGeohashPartitioned)
            {
                warnings.Add($"Estratégia Geohash reduz fan-out para ~{effectivePartitions} células vizinhas (vs. {physicalPartitions} partições totais). Inclua células vizinhas para evitar erro nas bordas.");
            }
            else
            {
                warnings.Add($"Query Cross-Partition em {physicalPartitions} partições físicas: adicione filtro pela Partition Key no WHERE para evitar ~2.5 RUs por partição.");
            }
        }

        var isSpatialQuery = Regex.IsMatch(sql, @"\bST_(DISTANCE|WITHIN|INTERSECTS|AREA|ISVALID|ISVALIDDETAILED)\s*\(", RegexOptions.IgnoreCase);
        if (isSpatialQuery)
        {
            if (!request.HasSpatialIndex && !isFullScan)
            {
                indexScanPenaltyRu += Math.Max(4.0, totalItems * 0.002 * docReadRu);
                warnings.Add("Indexação automática (/*) não cria índice espacial: configure 'spatialIndexes' no caminho GeoJSON para evitar varredura custosa.");
            }

            if (sql.Contains("ST_DISTANCE", StringComparison.OrdinalIgnoreCase))
            {
                if (sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
                {
                    indexScanPenaltyRu += request.HasSpatialIndex ? 0.85 : 6.0;
                }
                warnings.Add("ST_DISTANCE mede distância geográfica em linha reta, não distância rodoviária nem ETA.");
            }
        }

        if (Regex.IsMatch(sql, @"\b(UPPER|LOWER|CONTAINS)\s*\(", RegexOptions.IgnoreCase))
        {
            indexScanPenaltyRu += Math.Max(2.0, totalItems * 0.0005 * docReadRu);
            warnings.Add("Funções como UPPER()/LOWER()/CONTAINS() no WHERE podem causar varredura de índice; considere ComputedProperties indexadas.");
        }

        if (parsed?.OrderByFields is { Length: > 1 } &&
            !HasMatchingCompositeIndex(parsed.OrderByFields, request.CompositeIndexes, parsed.FromAlias))
        {
            indexScanPenaltyRu += 5.0;
            warnings.Add("ORDER BY em múltiplas propriedades sem CompositeIndex correspondente falha no Cosmos DB real com HTTP 400 (SubStatus 1004).");
        }

        if (sql.Contains("OFFSET ", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("OFFSET/LIMIT com deslocamentos altos aumenta consumo de RUs; prefira Continuation Tokens.");
        }

        var consistencyMult =
            string.Equals(request.ConsistencyLevel, "Strong", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(request.ConsistencyLevel, "BoundedStaleness", StringComparison.OrdinalIgnoreCase)
                ? 2.0
                : 1.0;

        if (consistencyMult > 1.0)
        {
            warnings.Add($"Consistência '{request.ConsistencyLevel}' dobra (~2x) o consumo de RUs nas leituras.");
        }

        var totalRu = (baseRu + resultScanRu + crossPartitionRu + indexScanPenaltyRu) * consistencyMult;

        return new QueryCostBreakdown(
            Math.Round(baseRu, 2),
            Math.Round(docReadRu, 2),
            Math.Round(resultScanRu, 2),
            Math.Round(crossPartitionRu, 2),
            Math.Round(indexScanPenaltyRu, 2),
            consistencyMult,
            Math.Round(totalRu, 2),
            isCrossPartition,
            isFullScan);
    }

    private static bool FiltersByPartitionKey(string sql, IReadOnlyList<string>? pkPaths)
    {
        if (pkPaths == null || pkPaths.Count == 0)
            return false;

        var whereIdx = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIdx < 0)
            return false;

        var whereClause = sql[whereIdx..];
        foreach (var path in pkPaths)
        {
            var leaf = path.Trim('/').Split('/').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(leaf) &&
                Regex.IsMatch(whereClause, $@"\b{Regex.Escape(leaf)}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMatchingCompositeIndex(
        OrderByField[] orderByFields,
        List<List<EmulatorCompositePath>> compositeIndexes,
        string? fromAlias)
    {
        if (compositeIndexes.Count == 0) return false;
        var aliasPrefix = (fromAlias ?? "c") + ".";

        var requested = orderByFields
            .Select(f =>
            {
                var raw = f.Field ?? string.Empty;
                if (raw.StartsWith(aliasPrefix, StringComparison.OrdinalIgnoreCase))
                    raw = raw[aliasPrefix.Length..];
                return ("/" + raw.TrimStart('/').Replace('.', '/'), f.Ascending);
            })
            .ToList();

        return compositeIndexes.Any(idxSet =>
            idxSet.Count >= requested.Count &&
            requested.Select((req, i) =>
            {
                var cp = idxSet[i];
                var cpAsc = !string.Equals(cp.Order, "descending", StringComparison.OrdinalIgnoreCase);
                return string.Equals(cp.Path, req.Item1, StringComparison.OrdinalIgnoreCase) && cpAsc == req.Ascending;
            }).All(match => match));
    }

    internal static double InterpolateDocumentReadRu(double sizeKb)
    {
        if (sizeKb <= DocReadTable[0].SizeKb)
            return DocReadTable[0].ReadRu;

        for (var i = 1; i < DocReadTable.Length; i++)
        {
            var prev = DocReadTable[i - 1];
            var curr = DocReadTable[i];
            if (sizeKb <= curr.SizeKb)
            {
                var t = (sizeKb - prev.SizeKb) / (curr.SizeKb - prev.SizeKb);
                return prev.ReadRu + t * (curr.ReadRu - prev.ReadRu);
            }
        }

        var last = DocReadTable[^1];
        return last.ReadRu * (sizeKb / last.SizeKb);
    }

    private static MonthlyCostEstimate CalculateServerless(
        CosmosRegionPricing region,
        double totalMonthlyMillionRus,
        QueryCostSimulationRequest req)
    {
        var az = req.AvailabilityZones ? 1.25 : 1.0;
        var regions = 1 + Math.Max(0, req.AdditionalRegionsCount);
        var processing = Math.Round(totalMonthlyMillionRus * region.ServerlessPerMillionRuUsd * az, 2);
        var storage = Math.Round(Math.Max(0, req.StorageGb) * region.StoragePerGbMonthUsd * regions, 2);
        return new MonthlyCostEstimate(
            "Serverless",
            processing,
            storage,
            Math.Round(processing + storage, 2),
            null,
            "Paga apenas pelas RUs consumidas (sem free tier).");
    }

    private static MonthlyCostEstimate CalculateProvisioned(
        CosmosRegionPricing region,
        int provisionedRu,
        QueryCostSimulationRequest req)
    {
        var az = req.AvailabilityZones ? 1.25 : 1.0;
        var regions = 1 + Math.Max(0, req.AdditionalRegionsCount);
        var multiWrite = req.MultiRegionWrites && regions > 1 ? 2.0 : 1.0;

        var billableRu = req.ApplyFreeTier ? Math.Max(0, provisionedRu - 1000) : provisionedRu;
        var billableGb = req.ApplyFreeTier ? Math.Max(0, req.StorageGb - 25.0) : Math.Max(0, req.StorageGb);

        var processing = Math.Round((billableRu / 1000.0) * region.Monthly1000RuProvisionedUsd * az * regions * multiWrite, 2);
        var storage = Math.Round(billableGb * region.StoragePerGbMonthUsd * regions, 2);

        return new MonthlyCostEstimate(
            "Provisioned",
            processing,
            storage,
            Math.Round(processing + storage, 2),
            null,
            $"Capacidade fixa de {provisionedRu} RU/s (730h/mês{(req.ApplyFreeTier ? ", com Free Tier" : "")}).");
    }

    private static MonthlyCostEstimate CalculateAutoscale(
        CosmosRegionPricing region,
        int recommendedRu,
        QueryCostSimulationRequest req)
    {
        var az = req.AvailabilityZones ? 1.25 : 1.0;
        var regions = 1 + Math.Max(0, req.AdditionalRegionsCount);
        var multiWrite = req.MultiRegionWrites && regions > 1 ? 2.0 : 1.0;

        var maxRu = Math.Max(1000, recommendedRu);
        var billableMaxRu = req.ApplyFreeTier ? Math.Max(0, maxRu - 1000) : maxRu;
        var billableGb = req.ApplyFreeTier ? Math.Max(0, req.StorageGb - 25.0) : Math.Max(0, req.StorageGb);

        var minProcessing = Math.Round((billableMaxRu * 0.10 / 1000.0) * region.Monthly1000RuProvisionedUsd * CosmosRegionalPricingCatalog.AutoscaleMultiplier * az * regions * multiWrite, 2);
        var maxProcessing = Math.Round((billableMaxRu / 1000.0) * region.Monthly1000RuProvisionedUsd * CosmosRegionalPricingCatalog.AutoscaleMultiplier * az * regions * multiWrite, 2);
        var storage = Math.Round(billableGb * region.StoragePerGbMonthUsd * regions, 2);

        return new MonthlyCostEstimate(
            "Autoscale",
            minProcessing,
            storage,
            Math.Round(minProcessing + storage, 2),
            Math.Round(maxProcessing + storage, 2),
            $"Escala de {maxRu / 10} a {maxRu} RU/s (tarifa 1.5x sobre o pico por hora).");
    }

    public static GeoFleetSimulationResult SimulateFleet(GeoFleetSimulationRequest request)
    {
        var region = CosmosRegionalPricingCatalog.GetOrDefault(request.Region);
        var baseline = CosmosRegionalPricingCatalog.GetOrDefault(CosmosRegionalPricingCatalog.BaselineRegionId);

        var vehicles = Math.Max(1, request.Vehicles);
        var interval = Math.Max(0.1, request.IntervalSeconds);
        var ruPerWrite = Math.Max(1.0, request.RuPerWrite);
        var days = Math.Clamp(request.DaysInMonth, 1, 31);
        var peakMult = Math.Max(1.0, request.PeakMultiplier);
        var qps = Math.Max(0.0, request.QueriesPerSecond);
        var ruPerRead = Math.Max(0.0, request.RuPerReadQuery);
        var docKb = Math.Max(0.2, request.AvgDocumentSizeKb);
        var retentionDays = Math.Max(0, request.RetentionDays);

        var writesPerSecond = vehicles / interval;
        var writesPerMonth = writesPerSecond * 86400.0 * days;
        var writeRuPerSecond = writesPerSecond * ruPerWrite;
        var writeRuPerMonth = writesPerMonth * ruPerWrite;

        var readRuPerSecond = qps * ruPerRead;
        var readRuPerMonth = readRuPerSecond * 86400.0 * days;

        var avgTotalRuPerSec = writeRuPerSecond + readRuPerSecond;
        var peakTotalRuPerSec = avgTotalRuPerSec * peakMult;
        var totalMonthlyRus = writeRuPerMonth + readRuPerMonth;
        var totalMonthlyMillionRus = totalMonthlyRus / 1_000_000.0;

        // Estado atual + histórico retido pelo TTL + ~15% overhead de índice espacial
        var currentStateKb = vehicles * docKb;
        var historyKb = retentionDays > 0 ? writesPerSecond * 86400.0 * retentionDays * docKb : 0.0;
        var estimatedStorageGb = Math.Round(((currentStateKb + historyKb) * 1.15) / (1024.0 * 1024.0), 4);

        var recommendedRu = Math.Max(400, (int)(Math.Ceiling(peakTotalRuPerSec / 100.0) * 100));

        var simReq = new QueryCostSimulationRequest
        {
            Region = region.Id,
            StorageGb = estimatedStorageGb,
            ApplyFreeTier = request.ApplyFreeTier
        };

        var serverless = CalculateServerless(region, totalMonthlyMillionRus, simReq);
        var provisioned = CalculateProvisioned(region, recommendedRu, simReq);
        var autoscale = CalculateAutoscale(region, recommendedRu, simReq);
        var baselineServerless = CalculateServerless(baseline, totalMonthlyMillionRus, simReq);

        var warnings = new List<string>();
        if (request.ApplyFreeTier)
            warnings.Add("Free Tier (1.000 RU/s e 25 GB) aplica-se apenas ao modo Provisionado/Autoscale, não ao Serverless.");
        if (interval <= 2.0)
            warnings.Add($"Frequência alta de GPS ({interval:0.#}s) multiplica gravações e manutenção do índice espacial; avalie envio adaptativo (parado vs em movimento).");
        if (peakMult > 1.2)
            warnings.Add($"Dimensionamento Provisionado baseado no pico ({Math.Round(peakTotalRuPerSec, 1)} RU/s) reduz risco de HTTP 429; a média ({Math.Round(avgTotalRuPerSec, 1)} RU/s) pode não absorver rajadas.");
        if (retentionDays > 7)
            warnings.Add($"Retenção de histórico GPS por {retentionDays} dias gera ~{estimatedStorageGb:0.##} GB; configure TTL no container de telemetria.");

        return new GeoFleetSimulationResult(
            region,
            baseline,
            Math.Round(writesPerSecond, 2),
            Math.Round(writesPerMonth, 0),
            Math.Round(writeRuPerSecond, 2),
            Math.Round(writeRuPerMonth, 0),
            Math.Round(readRuPerSecond, 2),
            Math.Round(readRuPerMonth, 0),
            Math.Round(avgTotalRuPerSec, 2),
            Math.Round(peakTotalRuPerSec, 2),
            Math.Round(totalMonthlyRus, 0),
            Math.Round(totalMonthlyMillionRus, 3),
            estimatedStorageGb,
            recommendedRu,
            serverless,
            provisioned,
            autoscale,
            baselineServerless,
            warnings);
    }
}
