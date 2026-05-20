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
  opsSummary: JsonRecord;
  recentFailures: JsonRecord[];
  jobSummary: JsonRecord;
  thumbnailPublishStatus: JsonRecord;
  analyticsInsights: JsonRecord[];
  platformSummary: JsonRecord[];
  contentPerformance: JsonRecord[];
  aiOptimizationRecommendations: JsonRecord[];
  aiOptimizationPendingApproval: JsonRecord[];
  optimizationPlan: JsonRecord;
  celestialAssetStatus: JsonRecord;
  partialData: boolean;
  lastRefreshedAt: string;
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
  runAiOptimization: (pipelineRunId: string, force = false) => request<JsonRecord>(`/api/ai-optimization/run/${encodeURIComponent(pipelineRunId)}?force=${force}`, { method: 'POST' }),
  initializeAnalytics: (pipelineRunId: string, force = false) => request<JsonRecord>(`/api/analytics/initialize/${encodeURIComponent(pipelineRunId)}?force=${force}`, { method: 'POST' }),
  backfillIntelligence: (pipelineRunId: string, force = false) => request<JsonRecord>(`/api/intelligence/backfill/${encodeURIComponent(pipelineRunId)}?force=${force}`, { method: 'POST' }),
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
  ,getOpsRuns: () => request<PipelineRun[]>('/api/ops/runs')
  ,getOpsRun: (pipelineRunId: string) => request<PipelineRun>(`/api/ops/run/${encodeURIComponent(pipelineRunId)}`)
  ,getOpsFailures: () => request<JsonRecord[]>('/api/ops/failures')
  ,getOpsSummary: () => request<JsonRecord>('/api/ops/summary')
  ,getOpsPipelinesRecent: () => request<PipelineRun[]>('/api/ops/pipelines/recent')
  ,getOpsPipelineStages: (id: string) => request<PipelineStage[]>(`/api/ops/pipelines/${encodeURIComponent(id)}/stages`)
  ,getOpsFailuresRecent: () => request<JsonRecord[]>('/api/ops/failures/recent')
  ,getOpsJobsSummary: () => request<JsonRecord>('/api/ops/jobs/summary')
  ,getPipelinesRecent: () => request<PipelineRun[]>('/api/pipelines/recent')
  ,getPipelineById: (id: string) => request<PipelineRun>(`/api/pipelines/${encodeURIComponent(id)}`)
  ,getThumbnailPublishStatus: (runId: string) => request<JsonRecord>(`/api/pipeline/${encodeURIComponent(runId)}/thumbnail-publish-status`)
  ,resumePipeline: (pipelineRunId: string) => request<JsonRecord>(`/api/pipeline/resume/${encodeURIComponent(pipelineRunId)}`, { method: 'POST' })
  ,retryPublish: (pipelineRunId: string, platform: string) => request<JsonRecord>(`/api/pipeline/retry-publish/${encodeURIComponent(pipelineRunId)}?platform=${encodeURIComponent(platform)}`, { method: 'POST' })
  ,retryYoutubePublish: (pipelineRunId: string, asset = 'all') => request<JsonRecord>(`/api/youtubepublish/${encodeURIComponent(pipelineRunId)}?asset=${encodeURIComponent(asset)}`, { method: 'POST' })
  ,retryMetaPublish: (pipelineRunId: string, asset = 'all') => request<JsonRecord>(`/api/metapublish/${encodeURIComponent(pipelineRunId)}?asset=${encodeURIComponent(asset)}`, { method: 'POST' })
  ,getSchedulerEventPlan: (regionId: string, date: string) => request<JsonRecord>(`/api/scheduler/event-plan?regionId=${encodeURIComponent(regionId)}&date=${encodeURIComponent(date)}`)
  ,enableSchedule: (scheduleName: string) => request<JsonRecord>(`/api/scheduler/enable/${encodeURIComponent(scheduleName)}`, { method: 'POST' })
  ,disableSchedule: (scheduleName: string) => request<JsonRecord>(`/api/scheduler/disable/${encodeURIComponent(scheduleName)}`, { method: 'POST' })
  ,enableRegion: (regionId: string) => request<JsonRecord>(`/api/regions/${encodeURIComponent(regionId)}/enable`, { method: 'POST' })
  ,disableRegion: (regionId: string) => request<JsonRecord>(`/api/regions/${encodeURIComponent(regionId)}/disable`, { method: 'POST' })
  ,getEventById: (eventId: string) => request<AstroEvent>(`/api/events/${encodeURIComponent(eventId)}`)
  ,refreshEvents: () => request<JsonRecord>('/api/events/refresh', { method: 'POST' })
  ,generateEvent: (eventId: string) => request<JsonRecord>(`/api/events/${encodeURIComponent(eventId)}/generate`, { method: 'POST' })
  ,getGeneratedEvents: () => request<AstroEvent[]>('/api/events/generated')
  ,getAnalyticsInsights: () => request<JsonRecord[]>('/api/analytics/insights')
  ,getAnalyticsPlatformSummary: () => request<JsonRecord[]>('/api/analytics/platform-summary')
  ,getAnalyticsContentPerformance: () => request<JsonRecord[]>('/api/analytics/content-performance')
  ,getAnalyticsRecent: () => request<JsonRecord[]>('/api/analytics/recent')
  ,getAnalyticsPlatform: (platform: string) => request<JsonRecord>(`/api/analytics/platform/${encodeURIComponent(platform)}`)
  ,getAnalyticsRun: (pipelineRunId: string) => request<JsonRecord>(`/api/analytics/run/${encodeURIComponent(pipelineRunId)}`)
  ,collectAnalyticsNow: () => request<JsonRecord>('/api/analytics/collect-now', { method: 'POST' })
  ,getAnalyticsTopPerforming: () => request<JsonRecord[]>('/api/analytics/top-performing')
  ,getAnalyticsYoutubeVideo: (videoId: string) => request<JsonRecord>(`/api/analytics/youtube/${encodeURIComponent(videoId)}`)
  ,getAiOptimizationRecommendations: () => request<JsonRecord[]>('/api/ai-optimization/recommendations')
  ,generateAiOptimizationNow: () => request<JsonRecord>('/api/ai-optimization/generate-now', { method: 'POST' })
  ,getAiOptimizationPendingApproval: () => request<JsonRecord[]>('/api/ai-optimization/pending-approval')
  ,applyAiOptimizationApproved: () => request<JsonRecord>('/api/ai-optimization/apply-approved', { method: 'POST' })
  ,rejectAiOptimization: (payload: JsonRecord = {}) => request<JsonRecord>('/api/ai-optimization/reject', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
  ,getAiOptimizationTrends: (date: string) => request<JsonRecord>(`/api/ai-optimization/trends/${encodeURIComponent(date)}`)
  ,getOptimizationPlan: (location: string, platform: string) => request<JsonRecord>(`/api/optimization/plan?location=${encodeURIComponent(location)}&platform=${encodeURIComponent(platform)}`)
  ,applyOptimizationPreview: (payload: JsonRecord) => request<JsonRecord>('/api/optimization/apply-preview', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })
  ,getYoutubeTokenHealth: () => request<JsonRecord>('/api/tokenhealth/youtube')
  ,getMetaTokenHealth: () => request<JsonRecord>('/api/tokenhealth/meta')
  ,getCelestialAssetStatus: () => request<JsonRecord>('/api/assets/celestial/status')
  ,refreshCelestialAssetStatus: () => request<JsonRecord>('/api/assets/celestial/refresh', { method: 'POST' })
  ,getCelestialAsset: (objectKey: string) => request<JsonRecord>(`/api/assets/celestial/${encodeURIComponent(objectKey)}`)
  ,checkHealth: () => request<JsonRecord>('/health')
  ,checkReadyHealth: () => request<JsonRecord>('/health/ready')
};

function arrayFrom<T>(value: unknown): T[] {
  if (Array.isArray(value)) return value as T[];
  if (value && typeof value === 'object' && Array.isArray((value as { items?: unknown[] }).items)) return (value as { items: T[] }).items;
  return [];
}


function firstDefined<T>(...values: T[]): T | undefined {
  return values.find((value) => value !== undefined && value !== null);
}

function asRecord(value: unknown): JsonRecord {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as JsonRecord : {};
}

function asString(value: unknown, fallback = 'Not available') {
  if (value === undefined || value === null || value === '') return fallback;
  return String(value);
}

function normalizeRegion(raw: unknown): Region {
  const item = asRecord(raw);
  return {
    id: asString(firstDefined(item.id, item.regionId, item.name), 'unknown-region'),
    name: asString(firstDefined(item.name, item.regionName, item.locationName), 'Not available'),
    displayName: asString(firstDefined(item.displayName, item.regionDisplayName), 'Not available'),
    timezone: asString(firstDefined(item.timezone, item.timeZone), 'Not available'),
    language: asString(firstDefined(item.language, item.locale), 'Not available'),
    latitude: Number(firstDefined(item.latitude, item.lat) ?? NaN),
    longitude: Number(firstDefined(item.longitude, item.lng) ?? NaN),
    enabled: Boolean(firstDefined(item.enabled, item.isEnabled) ?? false),
    localRunTime: item.localRunTime as string | undefined,
    nextPlannedRunUtc: asString(firstDefined(item.nextPlannedRunUtc, item.nextRunAt), 'Not available'),
    nextTargetDate: asString(item.nextTargetDate, 'Not available')
  };
}

function normalizePipelineRun(raw: unknown): PipelineRun {
  const item = asRecord(raw);
  const runId = asString(firstDefined(item.runId, item.pipelineRunId, item.id), 'unknown-run');
  const publishedUrls = arrayFrom<unknown>(firstDefined(item.publishedUrls, item.urls, item.links)).map((url) => asString(url)).filter((url) => safePublicUrl(url));
  return {
    ...item,
    runId,
    pipelineRunId: asString(firstDefined(item.pipelineRunId, item.runId, item.id), runId),
    id: asString(firstDefined(item.id, item.pipelineRunId, item.runId), runId),
    regionId: asString(firstDefined(item.regionId, item.locationId), 'Not available'),
    regionName: asString(firstDefined(item.regionName, item.locationName), 'Not available'),
    locationName: asString(firstDefined(item.locationName, item.regionName), 'Not available'),
    contentType: asString(firstDefined(item.contentType, item.platformContentType), 'Not available'),
    status: asString(firstDefined(item.status, item.runStatus), 'unknown'),
    runStatus: asString(firstDefined(item.runStatus, item.status), 'unknown'),
    startedAt: asString(firstDefined(item.startedAt, item.startedUtc, item.actualRunUtc), 'Not available'),
    startedUtc: asString(firstDefined(item.startedUtc, item.startedAt, item.actualRunUtc), 'Not available'),
    completedUtc: asString(firstDefined(item.completedUtc, item.completedAt), 'Not available'),
    publishedUrls,
    warnings: arrayFrom<unknown>(firstDefined(item.warnings, item.messages)).map((v) => String(v))
  } as PipelineRun;
}

function safePublicUrl(url: string) {
  try { const parsed = new URL(url); return parsed.protocol === 'http:' || parsed.protocol === 'https:'; } catch { return false; }
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
  return items.map((raw) => normalizeMediaItem({
    ...raw,
    id: String(firstDefined(raw.id, raw.mediaId, raw.url, (raw as JsonRecord).publishedUrl, (raw as JsonRecord).permalink, raw.previewUrl, raw.title) ?? 'content'),
    platform: asString(firstDefined(raw.platform, (raw as JsonRecord).provider), 'Not available'),
    status: asString(firstDefined(raw.status, (raw as JsonRecord).runStatus), 'unknown'),
    regionName: asString(firstDefined(raw.regionName, raw.locationName, (raw as JsonRecord).regionId), 'Not available'),
    createdAt: asString(firstDefined(raw.createdAt, raw.publishedAt, raw.publishedUtc), 'Not available'),
    publishedAt: asString(firstDefined(raw.publishedAt, raw.publishedUtc, raw.createdAt), 'Not available'),
    url: asString(firstDefined(raw.url, (raw as JsonRecord).publishedUrl, (raw as JsonRecord).permalink, raw.previewUrl), 'Not available'),
    views: Number(firstDefined(raw.views, (raw as JsonRecord).totalViews) ?? 0),
    engagementRate: Number(firstDefined(raw.engagementRate, (raw as JsonRecord).averageEngagementRate) ?? 0),
    contentType: asString(firstDefined(raw.contentType, raw.platformContentType), 'Not available')
  } as MediaItem)).filter((item): item is MediaItem => Boolean(item));
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
    ,opsSummary: {}
    ,recentFailures: []
    ,jobSummary: {}
    ,thumbnailPublishStatus: {}
    ,analyticsInsights: []
    ,platformSummary: []
    ,contentPerformance: []
    ,aiOptimizationRecommendations: []
    ,aiOptimizationPendingApproval: []
    ,optimizationPlan: {}
    ,celestialAssetStatus: {}
    ,partialData: false
    ,lastRefreshedAt: new Date().toISOString()
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
  const [opsResult, schedulerResult, regionsResult, upcomingEventsResult, topEventsResult, alertUpcomingResult, analyticsDashboardResult, analyticsSummaryResult, topContentResult, tokenHealthResult, opsRunsResult, recentFailuresResult, opsSummaryResult, jobsSummaryResult, analyticsInsightsResult, platformSummaryResult, contentPerformanceResult, aiRecsResult, aiPendingResult, optimizationPlanResult, celestialAssetStatusResult] = await Promise.all([
    capture('/api/ops/dashboard', api.getOpsDashboard, {} as OpsDashboard),
    capture('/api/scheduler/status', api.getSchedulerStatus, {} as SchedulerStatus),
    capture('/api/regions', api.getRegions, [] as Region[]),
    capture('/api/events/upcoming', api.getUpcomingEvents, [] as AstroEvent[]),
    capture('/api/events/top', api.getTopEvents, [] as AstroEvent[]),
    capture('/api/alerts/upcoming', () => api.getUpcomingAlerts(), [] as AlertUpcomingEvent[]),
    capture('/api/analytics/dashboard', api.getAnalyticsDashboard, {} as AnalyticsDashboard),
    capture('/api/analytics/summary', api.getAnalyticsSummary, {} as JsonRecord),
    capture('/api/analytics/top-content', api.getTopContent, [] as MediaItem[]),
    capture('/api/tokenhealth', api.getTokenHealth, [] as TokenHealthItem[]),
    capture('/api/ops/runs', api.getOpsRuns, [] as PipelineRun[]),
    capture('/api/ops/failures/recent', api.getOpsFailuresRecent, [] as JsonRecord[]),
    capture('/api/ops/summary', api.getOpsSummary, {} as JsonRecord),
    capture('/api/ops/jobs/summary', api.getOpsJobsSummary, {} as JsonRecord),
    capture('/api/analytics/insights', api.getAnalyticsInsights, [] as JsonRecord[]),
    capture('/api/analytics/platform-summary', api.getAnalyticsPlatformSummary, [] as JsonRecord[]),
    capture('/api/analytics/content-performance', api.getAnalyticsContentPerformance, [] as JsonRecord[]),
    capture('/api/ai-optimization/recommendations', api.getAiOptimizationRecommendations, [] as JsonRecord[]),
    capture('/api/ai-optimization/pending-approval', api.getAiOptimizationPendingApproval, [] as JsonRecord[]),
    capture('/api/optimization/plan', () => api.getOptimizationPlan('global', 'youtube'), {} as JsonRecord),
    capture('/api/assets/celestial/status', api.getCelestialAssetStatus, {} as JsonRecord)
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

  const regions = arrayFrom<unknown>(regionsResponse).map(normalizeRegion);
  const topContent = supportedMedia(arrayFrom<MediaItem>(topContentResponse));
  const analyticsDashboardWithSupportedPlatforms = {
    ...analyticsDashboard,
    platformBreakdown: supportedPlatformBreakdown(analyticsDashboard.platformBreakdown),
    topContent
  };
  const tokenHealthSummary = ops.tokenHealthSummary;
  const pipelineRuns = (opsRunsResult.value.length ? opsRunsResult.value : (ops.pipelineRuns ?? ops.recentPipelineRuns ?? schedulerPipelineRuns(scheduler))).map((run) => normalizePipelineRun(run));
  const latestRunId = pipelineRuns[0]?.runId ?? pipelineRuns[0]?.pipelineRunId ?? '';
  const [analyticsVideosResult, hookRecommendationsResult, publishingRecommendationsResult, thumbnailStatusResult] = await Promise.all([
    latestRunId ? capture(`/api/analytics/videos/${latestRunId}`, () => api.getAnalyticsVideos(latestRunId), [] as JsonRecord[]) : Promise.resolve({ value: [] as JsonRecord[], failed: false }),
    latestRunId ? capture(`/api/ai-optimization/hooks/${latestRunId}`, () => api.getAiOptimizationHooks(latestRunId), [] as JsonRecord[]) : Promise.resolve({ value: [] as JsonRecord[], failed: false }),
    latestRunId ? capture(`/api/ai-optimization/publishing/${latestRunId}`, () => api.getAiOptimizationPublishing(latestRunId), [] as JsonRecord[]) : Promise.resolve({ value: [] as JsonRecord[], failed: false }),
    latestRunId ? capture(`/api/pipeline/${latestRunId}/thumbnail-publish-status`, () => api.getThumbnailPublishStatus(latestRunId), {} as JsonRecord) : Promise.resolve({ value: {} as JsonRecord, failed: false })
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
    regions: (ops.regions ?? regions).map((region) => normalizeRegion(region)),
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
    publishingRecommendationsByRun: arrayFrom<JsonRecord>(publishingRecommendationsResult.value),
    opsSummary: opsSummaryResult.value,
    recentFailures: arrayFrom<JsonRecord>(recentFailuresResult.value),
    jobSummary: jobsSummaryResult.value,
    thumbnailPublishStatus: thumbnailStatusResult.value,
    analyticsInsights: arrayFrom<JsonRecord>(analyticsInsightsResult.value),
    platformSummary: arrayFrom<JsonRecord>(platformSummaryResult.value),
    contentPerformance: arrayFrom<JsonRecord>(contentPerformanceResult.value),
    aiOptimizationRecommendations: arrayFrom<JsonRecord>(aiRecsResult.value),
    aiOptimizationPendingApproval: arrayFrom<JsonRecord>(aiPendingResult.value),
    optimizationPlan: optimizationPlanResult.value,
    celestialAssetStatus: celestialAssetStatusResult.value,
    partialData: [opsResult,schedulerResult,regionsResult,upcomingEventsResult,topEventsResult,alertUpcomingResult,analyticsDashboardResult,analyticsSummaryResult,topContentResult,tokenHealthResult,opsRunsResult,recentFailuresResult,opsSummaryResult,jobsSummaryResult,analyticsInsightsResult,platformSummaryResult,contentPerformanceResult,aiRecsResult,aiPendingResult,optimizationPlanResult,celestialAssetStatusResult,analyticsVideosResult,hookRecommendationsResult,publishingRecommendationsResult,thumbnailStatusResult].some((r)=>r.failed),
    lastRefreshedAt: new Date().toISOString()
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

  const regions = arrayFrom<unknown>(regionsResult.value).map(normalizeRegion);
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
