import { API_BASE_URL } from '../config/environment.js';

export type Region = { id: string; name: string; timezone?: string; enabled?: boolean };
export type AstroEvent = { id: string; title: string; eventType?: string; regionName?: string; startsAt: string; visibility?: string; priority?: number };
export type MediaItem = { id: string; title: string; status?: string; regionName?: string; platform?: string; previewUrl?: string; durationSeconds?: number };
export type SchedulerStatus = { isEnabled: boolean; state: string; nextRunAt?: string; lastRunAt?: string };
export type AnalyticsSummary = { views?: number; watchTimeMinutes?: number; engagementRate?: number; topPlatform?: string };
export type TokenHealthItem = { provider: string; status: string; expiresAt?: string; message?: string };

export type MobileHomeData = {
  regions: Region[];
  topEvents: AstroEvent[];
  upcomingEvents: AstroEvent[];
  latestShorts: MediaItem[];
  scheduler: SchedulerStatus;
  analytics: AnalyticsSummary;
  tokenHealth: TokenHealthItem[];
};

const SECRET_FIELD_PATTERN = /(access|refresh)?token|secret|connectionstring|sas/i;

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

async function request<T>(path: string): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { headers: { Accept: 'application/json' } });
  } catch (error) {
    throw new ApiError(error instanceof Error ? error.message : 'Network request failed', 0);
  }

  const text = await response.text();
  const body = text ? JSON.parse(text) : undefined;
  if (!response.ok) {
    throw new ApiError(body?.message ?? `Request failed with status ${response.status}`, response.status);
  }
  return sanitizePayload(body as T);
}

export const api = {
  getOpsDashboard: () => request<Partial<{ latestShorts: MediaItem[]; scheduler: SchedulerStatus }>>('/api/ops/dashboard'),
  getSchedulerStatus: () => request<SchedulerStatus>('/api/scheduler/status'),
  getRegions: () => request<Region[]>('/api/regions'),
  getUpcomingEvents: () => request<AstroEvent[]>('/api/events/upcoming'),
  getTopEvents: () => request<AstroEvent[]>('/api/events/top'),
  getAnalyticsDashboard: () => request<AnalyticsSummary>('/api/analytics/dashboard'),
  getTokenHealth: () => request<TokenHealthItem[]>('/api/tokenhealth')
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
    scheduler: ops.scheduler ?? scheduler,
    analytics,
    tokenHealth
  };
}
