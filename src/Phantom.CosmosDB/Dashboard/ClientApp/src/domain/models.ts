namespace PhantomDashboard.Domain {
  export interface ContainerOverview {
    name: string;
    itemCount: number;
    throughput: number;
    defaultTtl?: number | null;
    partitionKeyPaths: string[];
    indexingMode: string;
    automaticIndexing: boolean;
    compositeIndexCount: number;
    uniqueKeys: string[][];
    computedProperties: { name: string; query: string }[];
  }

  export interface RegionPricing {
    id: string;
    country: string;
    regionName: string;
    flag: string;
    monthly1000RuProvisionedUsd: number;
    serverlessPerMillionRuUsd: number;
    storagePerGbMonthUsd: number;
    provisionedPer100RuPerHourUsd: number;
  }

  export interface CostOptions {
    Region: string;
    BillingMode: string;
    LogQueryCost: boolean;
    ApplyFreeTier: boolean;
    ConsistencyLevel: string;
    AvailabilityZones: boolean;
    AdditionalRegionsCount: number;
    MultiRegionWrites: boolean;
  }

  export interface OverviewPayload {
    databaseName: string;
    configFilePath?: string | null;
    totalItems: number;
    containerCount: number;
    chaosActive: boolean;
    costOptions: CostOptions;
    activeRegion: RegionPricing;
    containers: ContainerOverview[];
  }

  export interface LogEntry {
    sequence: number;
    timestampUtc: string;
    method: string;
    path: string;
    containerName?: string | null;
    statusCode: number;
    subStatusCode: number;
    requestCharge: number;
    estimatedCostUsd: number;
    regionId: string;
    durationMs: number;
    sqlQuery?: string | null;
    isChaosInjected: boolean;
  }

  export interface QueryCostBreakdown {
    baseQueryRu: number;
    documentReadRuUnit: number;
    resultScanRu: number;
    crossPartitionOverheadRu: number;
    indexScanPenaltyRu: number;
    consistencyMultiplier: number;
    totalRuPerExecution: number;
    isCrossPartition: boolean;
    isFullScan: boolean;
  }

  export interface MonthlyCostEstimate {
    model: string;
    processingUsd: number;
    storageUsd: number;
    totalUsd: number;
    maxTotalUsd?: number | null;
    notes: string;
  }

  export interface SimulationResult {
    region: RegionPricing;
    baselineRegion: RegionPricing;
    ruBreakdown: QueryCostBreakdown;
    totalMonthlyRus: number;
    totalMonthlyMillionRus: number;
    averageRuPerSecond: number;
    recommendedProvisionedRu: number;
    breakEvenMillionRusFor400Ru: number;
    regionalServerlessDiffPercentVsBaseline: number;
    regionalProvisionedDiffPercentVsBaseline: number;
    serverless: MonthlyCostEstimate;
    provisioned: MonthlyCostEstimate;
    autoscale: MonthlyCostEstimate;
    baselineServerless: MonthlyCostEstimate;
    baselineProvisioned: MonthlyCostEstimate;
    warnings: string[];
  }

  export interface SeedSummary {
    containerName: string;
    filePath: string;
    exists: boolean;
    fileBytes: number;
    seedItemCount: number;
    inMemoryCount: number;
  }

  export interface ContainerSeedResponse {
    containerName: string;
    filePath: string;
    exists: boolean;
    totalCount: number;
    items: Record<string, unknown>[];
    suggestedTemplate: Record<string, unknown>;
  }

  export interface SeedGenerationJobStatus {
    jobId: string;
    containerName: string;
    totalCount: number;
    processedCount: number;
    persistedCount: number;
    status: string;
    error?: string | null;
    savedFilePath?: string | null;
    percent: number;
    elapsedSeconds: number;
    itemsPerSecond: number;
  }
}
