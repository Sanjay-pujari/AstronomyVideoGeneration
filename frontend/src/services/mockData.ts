import type { DashboardData } from './api.js';

export const mockDashboardData: DashboardData = {
  latestVideos: [
    { id: 'vid-001', title: 'Jupiter and the Moon After Dusk', status: 'published', regionName: 'North America', platform: 'YouTube', createdAt: '2026-05-08T21:00:00Z' },
    { id: 'vid-002', title: 'Eta Aquarids Meteor Watch', status: 'rendered', regionName: 'Europe', platform: 'YouTube', createdAt: '2026-05-08T18:30:00Z' }
  ],
  latestShorts: [
    { id: 'short-001', title: 'Tonight: Saturn before sunrise', status: 'published', regionName: 'Pacific', platform: 'TikTok', durationSeconds: 42 },
    { id: 'short-002', title: '60-second sky map', status: 'queued', regionName: 'South America', platform: 'Instagram', durationSeconds: 58 }
  ],
  publishStatuses: [
    { platform: 'YouTube', status: 'healthy', lastPublishedAt: '2026-05-08T21:15:00Z' },
    { platform: 'TikTok', status: 'warning', message: 'Refresh required soon' },
    { platform: 'Instagram', status: 'queued' }
  ],
  scheduler: { isEnabled: true, state: 'running', nextRunAt: '2026-05-09T22:00:00Z', lastRunAt: '2026-05-08T22:00:00Z', activeRunId: 'run-20260509-na' },
  tokenHealth: [
    { provider: 'YouTube', status: 'healthy', expiresAt: '2026-06-08T00:00:00Z' },
    { provider: 'TikTok', status: 'warning', expiresAt: '2026-05-12T00:00:00Z' }
  ],
  analytics: { views: 124800, watchTimeMinutes: 9200, subscribersGained: 384, engagementRate: 6.4, topPlatform: 'YouTube' },
  regions: [
    { id: 'na', name: 'North America', timezone: 'America/New_York', enabled: true },
    { id: 'eu', name: 'Europe', timezone: 'Europe/London', enabled: true },
    { id: 'pac', name: 'Pacific', timezone: 'Pacific/Auckland', enabled: false }
  ],
  events: [
    { id: 'evt-1', title: 'Moon near Regulus', eventType: 'Conjunction', regionName: 'North America', startsAt: '2026-05-09T02:00:00Z', visibility: 'Clear western horizon' }
  ],
  upcomingEvents: [
    { id: 'evt-2', title: 'Venus low in twilight', eventType: 'Planet', regionName: 'Europe', startsAt: '2026-05-09T20:30:00Z', visibility: 'Low but bright' }
  ],
  topEvents: [
    { id: 'evt-3', title: 'Meteor shower peak window', eventType: 'Meteor shower', regionName: 'Global', startsAt: '2026-05-10T04:00:00Z', priority: 98 }
  ],
  pipelineRuns: [
    { runId: 'run-20260509-na', regionId: 'na', regionName: 'North America', status: 'rendering', stage: 'video-render', startedAt: '2026-05-09T20:00:00Z' }
  ]
};
