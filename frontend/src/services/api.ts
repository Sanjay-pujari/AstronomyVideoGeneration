import { API_BASE_URL } from './config.js';

export type Region = {
  id: string;
  name: string;
  timezone?: string;
  latitude?: number;
  longitude?: number;
  enabled?: boolean;
};

export type AstroEvent = {
  id: string;
  title: string;
  eventType?: string;
  regionId?: string;
  regionName?: string;
  startsAt: string;
  visibility?: string;
  priority?: number;
};

export type MediaItem = {
  id: string;
  title: string;
  status?: string;
  regionName?: string;
  createdAt?: string;
  publishedAt?: string;
  platform?: string;
  previewUrl?: string;
  durationSeconds?: number;
};

export type PublishStatus = {
  platform: string;
  status: string;
  lastPublishedAt?: string;
  message?: string;
};

export type SchedulerStatus = {
  isEnabled: boolean;
  state: string;
  nextRunAt?: string;
  lastRunAt?: string;
  activeRunId?: string;
};

export type TokenHealthItem = {
  provider: string;
  status: string;
  expiresAt?: string;
  message?: string;
};

export type AnalyticsSummary = {
  views?: number;
  watchTimeMinutes?: number;
  subscribersGained?: number;
  engagementRate?: number;
  topPlatform?: string;
};

export type PipelineRun = {
  runId: string;
  regionId?: string;
  regionName?: string;
  status: string;
  stage?: string;
  startedAt?: string;
  updatedAt?: string;
  message?: string;
};

export type OpsDashboard = {
  latestVideos: MediaItem[];
  latestShorts: MediaItem[];
  publishStatuses: PublishStatus[];
  scheduler: SchedulerStatus;
  tokenHealth: TokenHealthItem[];
  analytics: AnalyticsSummary;
  regions: Region[];
  events: AstroEvent[];
  pipelineRuns: PipelineRun[];
};

export type DashboardData = OpsDashboard & {
  upcomingEvents: AstroEvent[];
  topEvents: AstroEvent[];
};


const SECRET_FIELD_PATTERN = /(access|refresh)?token|secret|connectionstring|sas/i;

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

function removeSecrets<T>(value: T): T {
  if (Array.isArray(value)) {
    return value.map((item) => removeSecrets(item)) as T;
  }

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
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      headers: { Accept: 'application/json', ...(init?.headers ?? {}) },
      ...init
    });
  } catch (error) {
    throw new ApiError(error instanceof Error ? error.message : 'Network request failed', 0);
  }

  const text = await response.text();
  const body = text ? JSON.parse(text) : undefined;

  if (!response.ok) {
    throw new ApiError(body?.message ?? `Request failed with status ${response.status}`, response.status, removeSecrets(body));
  }

  return removeSecrets(body as T);
}

export const api = {
  getOpsDashboard: () => request<Partial<OpsDashboard>>('/api/ops/dashboard'),
  getPipelineStatus: (runId: string) => request<PipelineRun>(`/api/pipeline/status/${encodeURIComponent(runId)}`),
  getSchedulerStatus: () => request<SchedulerStatus>('/api/scheduler/status'),
  getRegions: () => request<Region[]>('/api/regions'),
  getUpcomingEvents: () => request<AstroEvent[]>('/api/events/upcoming'),
  getTopEvents: () => request<AstroEvent[]>('/api/events/top'),
  getAnalyticsDashboard: () => request<AnalyticsSummary>('/api/analytics/dashboard'),
  getTokenHealth: () => request<TokenHealthItem[]>('/api/tokenhealth'),
  requestManualRun: (regionId: string) =>
    request<PipelineRun>('/api/pipelines/run', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ regionId })
    })
};

export async function loadDashboardData(): Promise<DashboardData> {
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
    latestVideos: ops.latestVideos ?? [],
    latestShorts: ops.latestShorts ?? [],
    publishStatuses: ops.publishStatuses ?? [],
    scheduler: ops.scheduler ?? scheduler,
    tokenHealth: ops.tokenHealth ?? tokenHealth,
    analytics: ops.analytics ?? analytics,
    regions: ops.regions ?? regions,
    events: ops.events ?? upcomingEvents,
    pipelineRuns: ops.pipelineRuns ?? [],
    upcomingEvents,
    topEvents
  };
}

export { removeSecrets };
