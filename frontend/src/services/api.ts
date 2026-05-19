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
  publishedUtc?: string;
  collectedUtc?: string;
  platform?: string;
  contentType?: string;
  platformContentType?: string;
  mediaId?: string;
  previewUrl?: string;
  url?: string;
  durationSeconds?: number;
  watchDurationSeconds?: number;
  views?: number;
  engagement?: number;
  engagementRate?: number;
  performanceScore?: number;
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
  apiError?: string;
  analyticsSummaryCards: JsonRecord;
  analyticsVideoBreakdown: JsonRecord[];
  aiOptimizationByRun: JsonRecord[];
  publishingRecommendationsByRun: JsonRecord[];
};


export type AlertSubscriptionRequest = {
  email: string;
  phone?: string;
  preferredChannel?: 'Email' | 'Push' | 'WhatsAppLater';
  regionId: string;
  language: string;
  eventTypes: string[];
  preferredAlertTimeLocal: string;
  minimumEventScore?: number;
};

export type AlertPreferencesUpdateRequest = {
  eventTypes: string[];
  preferredAlertTimeLocal: string;
  minimumEventScore?: number;
  dailySkyGuideReminderEnabled: boolean;
  specialEventAlertsEnabled: boolean;
  preferredChannel?: 'Email' | 'Push' | 'WhatsAppLater';
  phone?: string;
  language?: string;
};

export type AlertSubscriber = {
  subscriberId: string;
  email: string;
  phone?: string;
  preferredChannel: string;
  regionId: string;
  language: string;
  isActive: boolean;
  preferences: AlertPreferences;
};

export type AlertPreferences = {
  subscriberId: string;
  eventTypes: string[];
  preferredAlertTimeLocal: string;
  minimumEventScore: number;
  dailySkyGuideReminderEnabled: boolean;
  specialEventAlertsEnabled: boolean;
};

export type AlertUpcomingEvent = AstroEvent & {
  eventId?: string;
  peakUtc?: string;
  endUtc?: string;
};

export type SafeSettingsSummary = {
  apiBaseUrl: string;
  timeoutMs: number;
  environment: string;
  productionApiConfigured: boolean;
  secretPolicy: string;
};

export type FrontendApiHealthEntry = {
  endpoint: string;
  success: boolean;
  responseTimeMs: number;
  fallbackUsed: boolean;
  status?: number;
  error?: string;
  checkedAt: string;
};

export type FrontendApiHealthReport = {
  generatedAt: string;
  apiBaseUrl: string;
  endpoints: FrontendApiHealthEntry[];
};

const SECRET_FIELD_PATTERN = /(access|refresh)?token|secret|connectionstring|connectionString|sas|signature|password|clientSecret|appSecret|key$/i;
const SUPPORTED_PLATFORMS = new Set(['youtube', 'facebook', 'instagram']);
const diagnostics: FrontendApiHealthEntry[] = [];

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

function nowMs() {
  return typeof performance !== 'undefined' ? performance.now() : Date.now();
}

function persistHealth() {
  if (typeof localStorage !== 'undefined') {
    localStorage.setItem('frontend-api-health.json', JSON.stringify(getFrontendApiHealth(), null, 2));
  }
}

function recordHealth(entry: FrontendApiHealthEntry) {
  diagnostics.unshift(entry);
  diagnostics.splice(50);
  persistHealth();
}

function markFallbackUsed(endpoint: string) {
  const entry = diagnostics.find((item) => item.endpoint === endpoint);
  if (entry) {
    entry.fallbackUsed = true;
    persistHealth();
  }
}

export function getFrontendApiHealth(): FrontendApiHealthReport {
  return {
    generatedAt: new Date().toISOString(),
    apiBaseUrl: API_BASE_URL,
    endpoints: diagnostics.slice()
  };
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), API_TIMEOUT_MS);
  const started = nowMs();

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
    recordHealth({ endpoint: path, success: false, responseTimeMs: Math.round(nowMs() - started), fallbackUsed: false, status: 0, error: message, checkedAt: new Date().toISOString() });
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
  recordHealth({ endpoint: path, success: response.ok, responseTimeMs: Math.round(nowMs() - started), fallbackUsed: false, status: response.status, error: response.ok ? undefined : 'HTTP request failed', checkedAt: new Date().toISOString() });
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
  getAnalyticsSummary: () => request<JsonRecord>('/api/analytics/summary'),
  getAnalyticsVideos: (pipelineRunId: string) => request<JsonRecord[]>(`/api/analytics/videos/${encodeURIComponent(pipelineRunId)}`),
  getAiOptimizationHooks: (pipelineRunId: string) => request<JsonRecord[]>(`/api/ai-optimization/hooks/${encodeURIComponent(pipelineRunId)}`),
  getAiOptimizationPublishing: (pipelineRunId: string) => request<JsonRecord[]>(`/api/ai-optimization/publishing/${encodeURIComponent(pipelineRunId)}`),
  getTopContent: () => request<MediaItem[] | { items?: MediaItem[] }>('/api/analytics/top-content'),
  getTokenHealth: () => request<TokenHealthItem[]>('/api/tokenhealth'),
  requestRegionRunNow: (regionId: string, force = false) => request<PipelineRun>(`/api/regions/${encodeURIComponent(regionId)}/run-now?force=${force}`, { method: 'POST' }),
  requestSchedulerRunNow: (scheduleName: string, force = false) => request<PipelineRun>(`/api/scheduler/run-now/${encodeURIComponent(scheduleName)}?force=${force}`, { method: 'POST' }),
  subscribeToAlerts: (payload: AlertSubscriptionRequest) => request<AlertSubscriber>('/api/alerts/subscribe', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }),
  getAlertPreferences: (subscriberId: string) => request<AlertSubscriber>(`/api/alerts/preferences/${encodeURIComponent(subscriberId)}`),
  updateAlertPreferences: (subscriberId: string, payload: AlertPreferencesUpdateRequest) => request<AlertSubscriber>(`/api/alerts/preferences/${encodeURIComponent(subscriberId)}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) }),
  getUpcomingAlerts: (regionId?: string) => request<AlertUpcomingEvent[]>(`/api/alerts/upcoming${regionId ? `?regionId=${encodeURIComponent(regionId)}` : ''}`),
  sendTestAlert: (subscriberId: string, eventId?: string) => request<{ notificationId: string; status: string; message: string }>('/api/alerts/test', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ subscriberId, eventId }) }),
  unsubscribeAlerts: (subscriberId: string) => request<{ subscriberId: string; isActive: boolean }>(`/api/alerts/unsubscribe/${encodeURIComponent(subscriberId)}`, { method: 'POST' })
};

function arrayFrom<T>(value: unknown): T[] {
  if (Array.isArray(value)) return value as T[];
  if (value && typeof value === 'object' && Array.isArray((value as { items?: unknown[] }).items)) return (value as { items: T[] }).items;
  return [];
}

function canonicalPlatform(value?: string) {
  const raw = String(value ?? '').trim();
  const normalized = raw.toLowerCase().replace(/[^a-z]/g, '');
  if (normalized === 'youtube') return 'YouTube';
  if (normalized === 'facebook') return 'Facebook';
  if (normalized === 'instagram') return 'Instagram';
  return undefined;
}

function isSupportedPlatform(value?: string) {
  const normalized = String(value ?? '').trim().toLowerCase().replace(/[^a-z]/g, '');
  return SUPPORTED_PLATFORMS.has(normalized);
}

function normalizeMediaItem(item: MediaItem): MediaItem | undefined {
  const platform = canonicalPlatform(item.platform);
  if (!platform) return undefined;
  return {
    ...item,
    id: String(item.id ?? item.mediaId ?? item.url ?? item.title ?? 'content'),
    title: item.title || item.mediaId || 'Untitled published content',
    platform,
    publishedAt: item.publishedAt ?? item.publishedUtc ?? item.createdAt,
    contentType: item.contentType ?? item.platformContentType
  };
}

function supportedMedia(items: MediaItem[]) {
  return items.map(normalizeMediaItem).filter((item): item is MediaItem => Boolean(item));
}

function contentTypeValue(item: MediaItem) {
  return String(item.contentType ?? item.platformContentType ?? '').toLowerCase();
}

function isShortForm(item: MediaItem) {
  const contentType = contentTypeValue(item);
  return contentType.includes('short') || contentType.includes('reel') || contentType.includes('story') || (item.durationSeconds !== undefined && item.durationSeconds <= 90);
}

function publishStatusesFromOps(ops: OpsDashboard): PublishStatus[] {
  const statuses = ops.publishStatuses?.length
    ? ops.publishStatuses
    : ops.platformPublishSummary && typeof ops.platformPublishSummary === 'object'
      ? Object.entries(ops.platformPublishSummary)
        .filter(([, value]) => value && typeof value === 'object' && !Array.isArray(value))
        .map(([platform, value]) => ({
          platform: platform.replace(/([A-Z])/g, ' $1').trim(),
          status: String(((value as JsonRecord).status as string | undefined) ?? 'unknown'),
          url: (value as JsonRecord).url as string | undefined
        }))
      : [];

  return statuses
    .filter((item) => isSupportedPlatform(item.platform))
    .map((item) => ({ ...item, platform: canonicalPlatform(item.platform) ?? item.platform }));
}

function tokenHealthFromSummary(summary?: TokenHealthSummary): TokenHealthItem[] {
  if (!summary) return [];
  return [
    { provider: 'YouTube', status: summary.youTubeValid === true ? 'healthy' : summary.youTubeValid === false ? 'error' : 'unknown', message: summary.expiryWarning ?? undefined },
    { provider: 'Meta', status: summary.metaValid === true ? 'healthy' : summary.metaValid === false ? 'error' : 'unknown', message: summary.expiryWarning ?? undefined }
  ];
}

function supportedPlatformBreakdown(items?: JsonRecord[]) {
  return (items ?? [])
    .filter((item) => isSupportedPlatform(String(item.platform ?? '')))
    .map((item) => ({ ...item, platform: canonicalPlatform(String(item.platform ?? '')) ?? String(item.platform ?? '') }));
}

function analyticsFromResponses(ops: OpsDashboard, analyticsDashboard: AnalyticsDashboard, topContent: MediaItem[]): AnalyticsSummary {
  const overall = analyticsDashboard.overallSummary ?? {};
  const opsAnalytics = ops.analytics ?? {};
  const bestPlatform = canonicalPlatform(String(overall.bestPerformingPlatform ?? ops.analyticsSummary?.bestPerformingPlatform ?? opsAnalytics.bestPlatform ?? opsAnalytics.topPlatform ?? ''));
  return {
    totalViews: Number(overall.totalViews ?? ops.analyticsSummary?.totalViews ?? opsAnalytics.totalViews ?? opsAnalytics.views ?? 0),
    views: Number(overall.totalViews ?? ops.analyticsSummary?.totalViews ?? opsAnalytics.totalViews ?? opsAnalytics.views ?? 0),
    totalEngagement: Number(overall.totalEngagement ?? ops.analyticsSummary?.totalEngagement ?? opsAnalytics.totalEngagement ?? 0),
    engagementRate: Number(overall.averageEngagementRate ?? opsAnalytics.engagementRate ?? 0),
    topPlatform: bestPlatform,
    bestPlatform,
    bestPerformingPlatform: bestPlatform,
    topContent,
    engagementTrends: []
  };
}

function schedulerPipelineRuns(scheduler: SchedulerStatus): PipelineRun[] {
  return scheduler.recentRuns?.map((run) => ({
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
}


function normalizeAlertEvents(events: AlertUpcomingEvent[] | unknown): AstroEvent[] {
  if (!Array.isArray(events)) return [];
  return events.map((event) => ({
    ...event,
    id: event.id ?? event.eventId ?? event.title,
    startsAt: event.startsAt ?? event.peakUtc ?? event.startUtc,
    startUtc: event.startUtc ?? event.startsAt,
    score: event.score ?? event.priority
  }));
}

function settingsSummary(): SafeSettingsSummary {
  return {
    apiBaseUrl: API_BASE_URL,
    timeoutMs: API_TIMEOUT_MS,
    environment: typeof location !== 'undefined' && location.hostname === 'localhost' ? 'local' : 'production',
    productionApiConfigured: API_BASE_URL.startsWith('https://'),
    secretPolicy: 'Secret-shaped fields are stripped before rendering; tokens, app secrets, connection strings, and SAS query strings are never shown.'
  };
}

export function emptyDashboardData(): DashboardData {
  return {
    ops: {},
    systemHealth: {},
    latestVideos: [],
    latestShorts: [],
    publishStatuses: [],
    scheduler: {},
    tokenHealth: [],
    analytics: analyticsFromResponses({}, {}, []),
    analyticsDashboard: {},
    regions: [],
    events: [],
    upcomingEvents: [],
    topEvents: [],
    pipelineRuns: [],
    settingsSummary: settingsSummary(),
    warnings: [],
    analyticsSummaryCards: {},
    analyticsVideoBreakdown: [],
    aiOptimizationByRun: [],
    publishingRecommendationsByRun: []
  };
}

type Capture<T> = { value: T; failed: boolean };

async function capture<T>(endpoint: string, loader: () => Promise<T>, fallback: T): Promise<Capture<T>> {
  try {
    return { value: await loader(), failed: false };
  } catch {
    markFallbackUsed(endpoint);
    return { value: fallback, failed: true };
  }
}

export async function loadDashboardData(): Promise<DashboardData> {
  const [opsResult, schedulerResult, regionsResult, upcomingEventsResult, topEventsResult, alertUpcomingResult, analyticsDashboardResult, analyticsSummaryResult, topContentResult, tokenHealthResult] = await Promise.all([
    capture('/api/ops/dashboard', api.getOpsDashboard, {} as OpsDashboard),
    capture('/api/scheduler/status', api.getSchedulerStatus, {} as SchedulerStatus),
    capture('/api/regions', api.getRegions, [] as Region[]),
    capture('/api/events/upcoming', api.getUpcomingEvents, [] as AstroEvent[]),
    capture('/api/events/top', api.getTopEvents, [] as AstroEvent[]),
    capture('/api/alerts/upcoming', () => api.getUpcomingAlerts(), [] as AlertUpcomingEvent[]),
    capture('/api/analytics/dashboard', api.getAnalyticsDashboard, {} as AnalyticsDashboard),
    capture('/api/analytics/summary', api.getAnalyticsSummary, {} as JsonRecord),
    capture('/api/analytics/top-content', api.getTopContent, [] as MediaItem[]),
    capture('/api/tokenhealth', api.getTokenHealth, [] as TokenHealthItem[])
  ]);
  const ops = opsResult.value;
  const scheduler = schedulerResult.value;
  const regionsResponse = regionsResult.value;
  const alertUpcomingEvents = normalizeAlertEvents(alertUpcomingResult.value);
  const upcomingEvents = alertUpcomingEvents.length ? alertUpcomingEvents : upcomingEventsResult.value;
  const topEvents = topEventsResult.value;
  const analyticsDashboard = analyticsDashboardResult.value;
  const topContentResponse = topContentResult.value;
  const tokenHealth = tokenHealthResult.value;
  const apiError = analyticsDashboardResult.failed || topContentResult.failed ? 'Analytics service temporarily unavailable.' : undefined;

  const regions = arrayFrom<Region>(regionsResponse);
  const topContent = supportedMedia(arrayFrom<MediaItem>(topContentResponse));
  const analyticsDashboardWithSupportedPlatforms = {
    ...analyticsDashboard,
    platformBreakdown: supportedPlatformBreakdown(analyticsDashboard.platformBreakdown),
    topContent
  };
  const tokenHealthSummary = ops.tokenHealthSummary;
  const pipelineRuns = ops.pipelineRuns ?? ops.recentPipelineRuns ?? schedulerPipelineRuns(scheduler);
  const latestRunId = pipelineRuns[0]?.runId ?? pipelineRuns[0]?.pipelineRunId ?? '';
  const [analyticsVideosResult, hookRecommendationsResult, publishingRecommendationsResult] = await Promise.all([
    latestRunId ? capture(`/api/analytics/videos/${latestRunId}`, () => api.getAnalyticsVideos(latestRunId), [] as JsonRecord[]) : Promise.resolve({ value: [] as JsonRecord[], failed: false }),
    latestRunId ? capture(`/api/ai-optimization/hooks/${latestRunId}`, () => api.getAiOptimizationHooks(latestRunId), [] as JsonRecord[]) : Promise.resolve({ value: [] as JsonRecord[], failed: false }),
    latestRunId ? capture(`/api/ai-optimization/publishing/${latestRunId}`, () => api.getAiOptimizationPublishing(latestRunId), [] as JsonRecord[]) : Promise.resolve({ value: [] as JsonRecord[], failed: false })
  ]);
  const videos = topContent.filter((item) => !isShortForm(item));
  const shorts = topContent.filter(isShortForm);

  return {
    ops,
    systemHealth: ops.systemHealthSummary ?? {},
    latestVideos: videos,
    latestShorts: shorts,
    publishStatuses: publishStatusesFromOps(ops),
    scheduler: ops.scheduler ?? ops.schedulerStatus ?? scheduler,
    tokenHealth: ops.tokenHealth?.length ? ops.tokenHealth : tokenHealth.length ? tokenHealth : tokenHealthFromSummary(tokenHealthSummary),
    tokenHealthSummary,
    analytics: analyticsFromResponses(ops, analyticsDashboardWithSupportedPlatforms, topContent),
    analyticsDashboard: analyticsDashboardWithSupportedPlatforms,
    regions: ops.regions ?? regions,
    events: ops.events ?? upcomingEvents,
    upcomingEvents,
    topEvents,
    pipelineRuns,
    settingsSummary: settingsSummary(),
    warnings: [...(ops.warnings ?? []), ...(scheduler.warnings ?? [])],
    apiError,
    analyticsSummaryCards: analyticsSummaryResult.value,
    analyticsVideoBreakdown: arrayFrom<JsonRecord>(analyticsVideosResult.value),
    aiOptimizationByRun: arrayFrom<JsonRecord>(hookRecommendationsResult.value),
    publishingRecommendationsByRun: arrayFrom<JsonRecord>(publishingRecommendationsResult.value)
  };
}

export async function loadPublicPortalData(): Promise<DashboardData> {
  const [regionsResult, upcomingEventsResult, topEventsResult, alertUpcomingResult, analyticsDashboardResult, topContentResult] = await Promise.all([
    capture('/api/regions', api.getRegions, [] as Region[]),
    capture('/api/events/upcoming', api.getUpcomingEvents, [] as AstroEvent[]),
    capture('/api/events/top', api.getTopEvents, [] as AstroEvent[]),
    capture('/api/alerts/upcoming', () => api.getUpcomingAlerts(), [] as AlertUpcomingEvent[]),
    capture('/api/analytics/dashboard', api.getAnalyticsDashboard, {} as AnalyticsDashboard),
    capture('/api/analytics/top-content', api.getTopContent, [] as MediaItem[])
  ]);

  const regions = arrayFrom<Region>(regionsResult.value);
  const alertUpcomingEvents = normalizeAlertEvents(alertUpcomingResult.value);
  const upcomingEvents = alertUpcomingEvents.length ? alertUpcomingEvents : upcomingEventsResult.value;
  const topEvents = topEventsResult.value;
  const topContent = supportedMedia(arrayFrom<MediaItem>(topContentResult.value));
  const analyticsDashboard = {
    ...analyticsDashboardResult.value,
    platformBreakdown: supportedPlatformBreakdown(analyticsDashboardResult.value.platformBreakdown),
    topContent
  };
  const videos = topContent.filter((item) => !isShortForm(item));
  const shorts = topContent.filter(isShortForm);

  return {
    ...emptyDashboardData(),
    latestVideos: videos,
    latestShorts: shorts,
    analytics: analyticsFromResponses({}, analyticsDashboard, topContent),
    analyticsDashboard,
    regions,
    events: upcomingEvents,
    upcomingEvents,
    topEvents,
    apiError: analyticsDashboardResult.failed || topContentResult.failed ? 'Content service temporarily unavailable.' : undefined
  };
}
