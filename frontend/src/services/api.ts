import { API_BASE_URL, API_TIMEOUT_MS } from './config.js';

export type JsonRecord = Record<string, unknown>;

export type Region = {
  id: string;
  name: string;
  displayName?: string;
  timezone?: string;
  language?: string;
  latitude?: number;
  longitude?: number;
  enabled?: boolean;
  localRunTime?: string;
  nextPlannedRunUtc?: string;
  nextTargetDate?: string;
};

export type AstroEvent = {
  id: string;
  title: string;
  eventType?: string;
  regionId?: string;
  regionName?: string;
  startsAt?: string;
  startUtc?: string;
  visibility?: string;
  priority?: number;
  score?: number;
  status?: string;
};

export type MediaItem = {
  id: string;
  title: string;
  status?: string;
  regionName?: string;
  locationName?: string;
  createdAt?: string;
  publishedAt?: string;
  platform?: string;
  previewUrl?: string;
  url?: string;
  durationSeconds?: number;
  views?: number;
  engagement?: number;
};

export type PublishStatus = {
  platform: string;
  status: string;
  lastPublishedAt?: string;
  message?: string;
  url?: string;
};

export type SchedulerStatus = {
  enabled?: boolean;
  isEnabled?: boolean;
  state?: string;
  maxConcurrentRuns?: number;
  queuedRuns?: number;
  activeRuns?: number;
  schedules?: SchedulerSchedule[];
  recentRuns?: SchedulerRunRecord[];
  nextRunAt?: string;
  nextPlannedRun?: string;
  lastRunAt?: string;
  activeRunId?: string;
  warnings?: string[];
};

export type SchedulerSchedule = {
  regionId?: string;
  name: string;
  enabled: boolean;
  locationName?: string;
  timezone?: string;
  localRunTime?: string;
  publishEnabled?: boolean;
  nextPlannedRunUtc?: string;
  nextTargetDate?: string;
};

export type SchedulerRunRecord = {
  regionId?: string;
  scheduleName: string;
  contentType?: string;
  targetDate?: string;
  plannedRunUtc?: string;
  actualRunUtc?: string;
  pipelineRunId?: string;
  status: string;
  skipReason?: string;
  locationName?: string;
  eventTitle?: string;
};

export type TokenHealthItem = {
  provider?: string;
  platform?: string;
  status?: string;
  isValid?: boolean;
  isConfigured?: boolean;
  expiresAt?: string;
  accountName?: string;
  message?: string;
  error?: string;
};

export type TokenHealthSummary = {
  youTubeValid?: boolean | null;
  metaValid?: boolean | null;
  expiryWarning?: string | null;
  warnings?: string[];
};

export type AnalyticsSummary = {
  views?: number;
  watchTimeMinutes?: number;
  subscribersGained?: number;
  engagementRate?: number;
  topPlatform?: string;
  totalViews?: number;
  totalEngagement?: number;
  bestPerformingPlatform?: string;
  bestRegion?: string;
  bestPlatform?: string;
  topContent?: MediaItem[];
  engagementTrends?: TrendPoint[];
};

export type TrendPoint = {
  label: string;
  value: number;
};

export type PipelineStage = {
  stageName: string;
  status: string;
  attemptCount?: number;
  maxAttempts?: number;
  startedUtc?: string;
  completedUtc?: string;
  lastError?: string;
};

export type PipelineRun = {
  runId: string;
  id?: string;
  pipelineRunId?: string;
  regionId?: string;
  regionName?: string;
  locationName?: string;
  contentType?: string;
  targetDate?: string;
  status?: string;
  runStatus?: string;
  stage?: string;
  startedAt?: string;
  startedUtc?: string;
  completedUtc?: string;
  updatedAt?: string;
  durationSeconds?: number;
  failedStage?: string;
  message?: string;
  lastError?: string;
  stages?: PipelineStage[];
  publishedUrls?: string[];
  warnings?: string[];
};

export type OpsDashboard = {
  latestVideos?: MediaItem[];
  latestShorts?: MediaItem[];
  publishStatuses?: PublishStatus[];
  scheduler?: SchedulerStatus;
  schedulerStatus?: SchedulerStatus;
  tokenHealth?: TokenHealthItem[];
  tokenHealthSummary?: TokenHealthSummary;
  analytics?: AnalyticsSummary;
  analyticsSummary?: JsonRecord;
  analyticsIntelligence?: JsonRecord;
  systemHealthSummary?: JsonRecord;
  platformPublishSummary?: JsonRecord;
  regions?: Region[];
  regionBreakdown?: JsonRecord[];
  events?: AstroEvent[];
  pipelineRuns?: PipelineRun[];
  recentPipelineRuns?: PipelineRun[];
  warnings?: string[];
};

export type AnalyticsDashboard = {
  overallSummary?: JsonRecord;
  platformBreakdown?: JsonRecord[];
  contentTypeBreakdown?: JsonRecord[];
  regionBreakdown?: JsonRecord[];
  trends?: JsonRecord;
  charts?: JsonRecord;
  insights?: JsonRecord[];
  topContent?: MediaItem[];
};

export type DashboardData = {
  ops: OpsDashboard;
  systemHealth: JsonRecord;
  latestVideos: MediaItem[];
  latestShorts: MediaItem[];
  publishStatuses: PublishStatus[];
  scheduler: SchedulerStatus;
  tokenHealth: TokenHealthItem[];
  tokenHealthSummary?: TokenHealthSummary;
  analytics: AnalyticsSummary;
  analyticsDashboard: AnalyticsDashboard;
  regions: Region[];
  events: AstroEvent[];
  upcomingEvents: AstroEvent[];
  topEvents: AstroEvent[];
  pipelineRuns: PipelineRun[];
  settingsSummary: SafeSettingsSummary;
  warnings: string[];
};

export type SafeSettingsSummary = {
  apiBaseUrl: string;
  timeoutMs: number;
  environment: string;
  productionApiConfigured: boolean;
  secretPolicy: string;
};

const SECRET_FIELD_PATTERN = /(access|refresh)?token|secret|connectionstring|connectionString|sas|signature|password|clientSecret|appSecret|key$/i;

export class ApiError extends Error {
  status: number;
  details?: unknown;

  constructor(message: string, status: number, details?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.details = details;
  }
}

export function removeSecrets<T>(value: T): T {
  if (Array.isArray(value)) return value.map((item) => removeSecrets(item)) as T;

  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([key]) => !SECRET_FIELD_PATTERN.test(key))
        .map(([key, item]) => [key, removeSecrets(item)])
    ) as T;
  }

  return value;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), API_TIMEOUT_MS);

  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      headers: { Accept: 'application/json', ...(init?.headers ?? {}) },
      ...init,
      signal: controller.signal
    });
  } catch (error) {
    const message = error instanceof DOMException && error.name === 'AbortError'
      ? `Request timed out after ${API_TIMEOUT_MS}ms`
      : error instanceof Error ? error.message : 'Network request failed';
    throw new ApiError(message, 0);
  } finally {
    clearTimeout(timeout);
  }

  const text = await response.text();
  let body: unknown;
  try {
    body = text ? JSON.parse(text) : undefined;
  } catch {
    body = { message: text || 'Invalid JSON response' };
  }

  const sanitized = removeSecrets(body);
  if (!response.ok) {
    const message = sanitized && typeof sanitized === 'object' && 'message' in sanitized
      ? String((sanitized as JsonRecord).message)
      : `Request failed with status ${response.status}`;
    throw new ApiError(message, response.status, sanitized);
  }

  return sanitized as T;
}

export const api = {
  getOpsDashboard: () => request<OpsDashboard>('/api/ops/dashboard'),
  getPipelineStatus: (runId: string) => request<PipelineRun>(`/api/pipeline/status/${encodeURIComponent(runId)}`),
  getSchedulerStatus: () => request<SchedulerStatus>('/api/scheduler/status'),
  getRegions: () => request<Region[] | { items?: Region[] }>('/api/regions'),
  getUpcomingEvents: () => request<AstroEvent[]>('/api/events/upcoming'),
  getTopEvents: () => request<AstroEvent[]>('/api/events/top'),
  getAnalyticsDashboard: () => request<AnalyticsDashboard>('/api/analytics/dashboard'),
  getTokenHealth: () => request<TokenHealthItem[]>('/api/tokenhealth'),
  requestRegionRunNow: (regionId: string, force = false) => request<PipelineRun>(`/api/regions/${encodeURIComponent(regionId)}/run-now?force=${force}`, { method: 'POST' }),
  requestSchedulerRunNow: (scheduleName: string, force = false) => request<PipelineRun>(`/api/scheduler/run-now/${encodeURIComponent(scheduleName)}?force=${force}`, { method: 'POST' })
};

function arrayFrom<T>(value: unknown): T[] {
  if (Array.isArray(value)) return value as T[];
  if (value && typeof value === 'object' && Array.isArray((value as { items?: unknown[] }).items)) return (value as { items: T[] }).items;
  return [];
}

function publishStatusesFromOps(ops: OpsDashboard): PublishStatus[] {
  if (ops.publishStatuses?.length) return ops.publishStatuses;
  const summary = ops.platformPublishSummary;
  if (!summary || typeof summary !== 'object') return [];
  return Object.entries(summary)
    .filter(([, value]) => value && typeof value === 'object' && !Array.isArray(value))
    .map(([platform, value]) => ({
      platform: platform.replace(/([A-Z])/g, ' $1').trim(),
      status: String(((value as JsonRecord).status as string | undefined) ?? 'unknown'),
      url: (value as JsonRecord).url as string | undefined
    }));
}

function tokenHealthFromSummary(summary?: TokenHealthSummary): TokenHealthItem[] {
  if (!summary) return [];
  return [
    { provider: 'YouTube', status: summary.youTubeValid === true ? 'healthy' : summary.youTubeValid === false ? 'error' : 'unknown', message: summary.expiryWarning ?? undefined },
    { provider: 'Meta', status: summary.metaValid === true ? 'healthy' : summary.metaValid === false ? 'error' : 'unknown', message: summary.expiryWarning ?? undefined }
  ];
}

function analyticsFromResponses(ops: OpsDashboard, analyticsDashboard: AnalyticsDashboard): AnalyticsSummary {
  const overall = analyticsDashboard.overallSummary ?? {};
  const opsAnalytics = ops.analytics ?? {};
  return {
    ...opsAnalytics,
    views: opsAnalytics.views ?? Number(overall.totalViews ?? ops.analyticsSummary?.totalViews ?? 0),
    totalViews: Number(overall.totalViews ?? ops.analyticsSummary?.totalViews ?? opsAnalytics.totalViews ?? opsAnalytics.views ?? 0),
    totalEngagement: Number(overall.totalEngagement ?? ops.analyticsSummary?.totalEngagement ?? opsAnalytics.totalEngagement ?? 0),
    engagementRate: opsAnalytics.engagementRate ?? Number(overall.averageEngagementRate ?? 0),
    topPlatform: opsAnalytics.topPlatform ?? String(overall.bestPerformingPlatform ?? ops.analyticsSummary?.bestPerformingPlatform ?? ''),
    bestPlatform: String(overall.bestPerformingPlatform ?? ops.analyticsSummary?.bestPerformingPlatform ?? ''),
    topContent: analyticsDashboard.topContent ?? arrayFrom<MediaItem>(ops.analyticsIntelligence && typeof ops.analyticsIntelligence === 'object' ? (ops.analyticsIntelligence as JsonRecord).topContent : undefined),
    engagementTrends: []
  };
}

export async function loadDashboardData(): Promise<DashboardData> {
  const [ops, scheduler, regionsResponse, upcomingEvents, topEvents, analyticsDashboard, tokenHealth] = await Promise.all([
    api.getOpsDashboard(),
    api.getSchedulerStatus(),
    api.getRegions(),
    api.getUpcomingEvents(),
    api.getTopEvents(),
    api.getAnalyticsDashboard(),
    api.getTokenHealth()
  ]);

  const regions = arrayFrom<Region>(regionsResponse);
  const tokenHealthSummary = ops.tokenHealthSummary;
  const pipelineRuns = ops.pipelineRuns ?? ops.recentPipelineRuns ?? scheduler.recentRuns?.map((run) => ({
    runId: run.pipelineRunId ?? run.scheduleName,
    pipelineRunId: run.pipelineRunId,
    regionId: run.regionId,
    regionName: run.locationName,
    contentType: run.contentType,
    targetDate: run.targetDate,
    status: run.status,
    startedUtc: run.actualRunUtc ?? run.plannedRunUtc,
    message: run.skipReason ?? run.eventTitle
  })) ?? [];

  return {
    ops,
    systemHealth: ops.systemHealthSummary ?? {},
    latestVideos: ops.latestVideos ?? [],
    latestShorts: ops.latestShorts ?? [],
    publishStatuses: publishStatusesFromOps(ops),
    scheduler: ops.scheduler ?? ops.schedulerStatus ?? scheduler,
    tokenHealth: ops.tokenHealth?.length ? ops.tokenHealth : tokenHealth.length ? tokenHealth : tokenHealthFromSummary(tokenHealthSummary),
    tokenHealthSummary,
    analytics: analyticsFromResponses(ops, analyticsDashboard),
    analyticsDashboard,
    regions: ops.regions ?? regions,
    events: ops.events ?? upcomingEvents,
    upcomingEvents,
    topEvents,
    pipelineRuns,
    settingsSummary: {
      apiBaseUrl: API_BASE_URL,
      timeoutMs: API_TIMEOUT_MS,
      environment: typeof location !== 'undefined' && location.hostname === 'localhost' ? 'local' : 'production',
      productionApiConfigured: API_BASE_URL.startsWith('https://'),
      secretPolicy: 'Secret-shaped fields are stripped before rendering; tokens, app secrets, connection strings, and SAS query strings are never shown.'
    },
    warnings: [...(ops.warnings ?? []), ...(scheduler.warnings ?? [])]
  };
}
