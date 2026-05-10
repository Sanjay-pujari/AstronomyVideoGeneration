import { API_BASE_URL, API_TIMEOUT_MS } from '../config/environment.js';

export type Region = { id: string; name: string; timezone?: string; language?: string; enabled?: boolean };
export type AstroEvent = {
  id: string;
  title: string;
  eventType?: string;
  regionName?: string;
  startsAt: string;
  visibility?: string;
  priority?: number;
  score?: number;
};
export type MediaPlatform = 'YouTube' | 'YouTube Shorts' | 'Facebook Reels' | 'Instagram Reels' | string;
export type MediaItem = {
  id: string;
  title: string;
  status?: string;
  regionName?: string;
  platform?: MediaPlatform;
  previewUrl?: string;
  externalUrl?: string;
  durationSeconds?: number;
  contentType?: 'daily-sky-guide' | 'long-video' | 'short' | 'reel' | string;
  publishedAt?: string;
};
export type SchedulerStatus = { isEnabled: boolean; state: string; nextRunAt?: string; lastRunAt?: string };
export type AnalyticsSummary = { views?: number; watchTimeMinutes?: number; engagementRate?: number; topPlatform?: string };
export type TokenHealthItem = { provider: string; status: string; expiresAt?: string; message?: string };
export type PipelineRunStatus = { runId: string; status: string; stage?: string; updatedAt?: string; platforms?: PlatformPublishStatus[] };
export type PlatformPublishStatus = { platform: string; status: string; publishedAt?: string; itemId?: string };
export type OpsDashboard = Partial<{
  latestShorts: MediaItem[];
  latestVideos: MediaItem[];
  latestPublished: MediaItem;
  latestDailySkyGuide: MediaItem;
  pipelineRuns: PipelineRunStatus[];
  platformStatuses: PlatformPublishStatus[];
  scheduler: SchedulerStatus;
}>;

export type MobileHomeData = {
  regions: Region[];
  topEvents: AstroEvent[];
  upcomingEvents: AstroEvent[];
  latestShorts: MediaItem[];
  latestVideos: MediaItem[];
  latestPublished?: MediaItem;
  latestDailySkyGuide?: MediaItem;
  pipelineRuns: PipelineRunStatus[];
  platformStatuses: PlatformPublishStatus[];
  scheduler: SchedulerStatus;
  analytics: AnalyticsSummary;
  tokenHealth: TokenHealthItem[];
  developmentMockData: boolean;
};

const SECRET_FIELD_PATTERN = /(access|refresh)?token|secret|connectionstring|sas|signature|clientsecret|apikey/i;
const BLOCKED_QUERY_PATTERN = /(sig|signature|se|sp|spr|sv|sr|srt|skoid|sktid|skt|ske|sks|skv|token|key|secret|code|client_secret)/i;

export class ApiError extends Error {
  status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export function sanitizePayload<T>(value: T): T {
  if (Array.isArray(value)) return value.map((item) => sanitizePayload(item)) as T;
  if (value && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .filter(([key]) => !SECRET_FIELD_PATTERN.test(key))
        .map(([key, item]) => [key, sanitizePayload(item)])
    ) as T;
  }
  return value;
}

export function createSafeExternalLink(url: string | undefined): string | undefined {
  if (!url) return undefined;
  try {
    const parsed = new URL(url);
    if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') return undefined;
    for (const key of Array.from(parsed.searchParams.keys())) {
      if (BLOCKED_QUERY_PATTERN.test(key)) parsed.searchParams.delete(key);
    }
    parsed.hash = '';
    return parsed.toString();
  } catch {
    return undefined;
  }
}

async function request<T>(path: string): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), API_TIMEOUT_MS);
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      headers: { Accept: 'application/json' },
      signal: controller.signal
    });
  } catch (error) {
    throw new ApiError(error instanceof Error && error.name === 'AbortError' ? 'Request timed out. Please try again.' : 'Network request failed. Please check your connection.', 0);
  } finally {
    clearTimeout(timeout);
  }

  const text = await response.text();
  const body = text ? sanitizePayload(JSON.parse(text)) : undefined;
  if (!response.ok) {
    const message = typeof body === 'object' && body && 'message' in body ? String((body as { message?: unknown }).message) : `Request failed with status ${response.status}`;
    throw new ApiError(message, response.status);
  }
  return body as T;
}

export const api = {
  getOpsDashboard: () => request<OpsDashboard>('/api/ops/dashboard'),
  getSchedulerStatus: () => request<SchedulerStatus>('/api/scheduler/status'),
  getRegions: () => request<Region[]>('/api/regions'),
  getUpcomingEvents: () => request<AstroEvent[]>('/api/events/upcoming'),
  getTopEvents: () => request<AstroEvent[]>('/api/events/top'),
  getAnalyticsDashboard: () => request<AnalyticsSummary>('/api/analytics/dashboard'),
  getTokenHealth: () => request<TokenHealthItem[]>('/api/tokenhealth'),
  getPipelineStatus: (runId: string) => request<PipelineRunStatus>(`/api/pipeline/status/${encodeURIComponent(runId)}`)
};

export async function loadMobileHomeData(): Promise<MobileHomeData> {
  const [ops, scheduler, regions, upcomingEvents, topEvents, analytics, tokenHealth] = await Promise.all([
    api.getOpsDashboard(),
    api.getSchedulerStatus(),
    api.getRegions(),
    api.getUpcomingEvents(),
    api.getTopEvents(),
    api.getAnalyticsDashboard(),
    api.getTokenHealth()
  ]);

  return {
    regions,
    topEvents,
    upcomingEvents,
    latestShorts: ops.latestShorts ?? [],
    latestVideos: ops.latestVideos ?? [],
    latestPublished: ops.latestPublished ?? ops.latestVideos?.[0] ?? ops.latestShorts?.[0],
    latestDailySkyGuide: ops.latestDailySkyGuide,
    pipelineRuns: ops.pipelineRuns ?? [],
    platformStatuses: ops.platformStatuses ?? [],
    scheduler: ops.scheduler ?? scheduler,
    analytics,
    tokenHealth,
    developmentMockData: false
  };
}
