import type { DashboardData } from './api.js';

export const mockDashboardData: DashboardData = {
  ops: {
    warnings: ['Mock telemetry is active until the API responds.'],
    systemHealthSummary: {
      ffmpegConfigured: true,
      stellariumConfigured: true,
      skyfieldSidecarReachable: true,
      azureBlobStorageConfigured: true,
      warnings: []
    }
  },
  systemHealth: {
    ffmpegConfigured: true,
    stellariumConfigured: true,
    skyfieldSidecarReachable: true,
    azureBlobStorageConfigured: true,
    warnings: []
  },
  latestVideos: [
    { id: 'vid-001', title: 'Jupiter and the Moon After Dusk', status: 'published', regionName: 'North America', platform: 'YouTube', createdAt: '2026-05-08T21:00:00Z', url: 'https://youtube.example/watch/vid-001?sig=hidden' },
    { id: 'vid-002', title: 'Eta Aquarids Meteor Watch', status: 'rendered', regionName: 'Europe', platform: 'YouTube', createdAt: '2026-05-08T18:30:00Z' }
  ],
  latestShorts: [
    { id: 'short-001', title: 'Tonight: Saturn before sunrise', status: 'published', regionName: 'Pacific', platform: 'TikTok', durationSeconds: 42, url: 'https://tiktok.example/@astropulse/video/short-001' },
    { id: 'short-002', title: '60-second sky map', status: 'queued', regionName: 'South America', platform: 'Instagram', durationSeconds: 58 }
  ],
  publishStatuses: [
    { platform: 'YouTube Long', status: 'healthy', lastPublishedAt: '2026-05-08T21:15:00Z', url: 'https://youtube.example/watch/vid-001?sv=sas-token' },
    { platform: 'TikTok', status: 'warning', message: 'Refresh required soon' },
    { platform: 'Instagram', status: 'queued' }
  ],
  scheduler: {
    enabled: true,
    isEnabled: true,
    state: 'running',
    queuedRuns: 1,
    activeRuns: 1,
    maxConcurrentRuns: 2,
    nextRunAt: '2026-05-09T22:00:00Z',
    lastRunAt: '2026-05-08T22:00:00Z',
    activeRunId: '11111111-1111-1111-1111-111111111111',
    schedules: [
      { regionId: 'na', name: 'nightly-na', enabled: true, locationName: 'North America', timezone: 'America/New_York', localRunTime: '21:00', nextPlannedRunUtc: '2026-05-09T22:00:00Z' },
      { regionId: 'eu', name: 'nightly-eu', enabled: true, locationName: 'Europe', timezone: 'Europe/London', localRunTime: '21:30', nextPlannedRunUtc: '2026-05-09T20:30:00Z' }
    ],
    recentRuns: []
  },
  tokenHealth: [
    { provider: 'YouTube', platform: 'YouTube', status: 'healthy', isValid: true, expiresAt: '2026-06-08T00:00:00Z' },
    { provider: 'Meta', platform: 'Meta', status: 'warning', isValid: true, expiresAt: '2026-05-12T00:00:00Z' }
  ],
  tokenHealthSummary: { youTubeValid: true, metaValid: true, expiryWarning: 'Meta expires within 7 days.', warnings: [] },
  analytics: {
    views: 124800,
    totalViews: 124800,
    totalEngagement: 8200,
    watchTimeMinutes: 9200,
    subscribersGained: 384,
    engagementRate: 6.4,
    topPlatform: 'YouTube',
    bestPlatform: 'YouTube',
    bestRegion: 'North America',
    topContent: [
      { id: 'vid-001', title: 'Jupiter and the Moon After Dusk', status: 'published', platform: 'YouTube', views: 84200, engagement: 6200 }
    ]
  },
  analyticsDashboard: {
    overallSummary: { totalViews: 124800, totalEngagement: 8200, averageEngagementRate: 6.4, bestPerformingPlatform: 'YouTube' },
    platformBreakdown: [
      { platform: 'YouTube', contentCount: 12, totalViews: 98200, averageEngagement: 7.1 },
      { platform: 'Instagram', contentCount: 8, totalViews: 18400, averageEngagement: 5.3 },
      { platform: 'TikTok', contentCount: 5, totalViews: 8200, averageEngagement: 4.9 }
    ],
    regionBreakdown: [
      { regionId: 'na', locationName: 'North America', runs: 24, views: 88200 },
      { regionId: 'eu', locationName: 'Europe', runs: 18, views: 28400 }
    ],
    trends: { engagementRate: [5.8, 6.1, 6.4], views: [28000, 41800, 55000] },
    topContent: [
      { id: 'vid-001', title: 'Jupiter and the Moon After Dusk', status: 'published', platform: 'YouTube', views: 84200, engagement: 6200 }
    ]
  },
  regions: [
    { id: 'na', name: 'North America', displayName: 'North America', timezone: 'America/New_York', language: 'en', enabled: true, localRunTime: '21:00' },
    { id: 'eu', name: 'Europe', displayName: 'Europe', timezone: 'Europe/London', language: 'en', enabled: true, localRunTime: '21:30' },
    { id: 'pac', name: 'Pacific', displayName: 'Pacific', timezone: 'Pacific/Auckland', language: 'en', enabled: false, localRunTime: '20:45' }
  ],
  events: [
    { id: 'evt-1', title: 'Moon near Regulus', eventType: 'Conjunction', regionName: 'North America', startsAt: '2026-05-09T02:00:00Z', visibility: 'Clear western horizon', score: 86, status: 'ready' }
  ],
  upcomingEvents: [
    { id: 'evt-2', title: 'Venus low in twilight', eventType: 'Planet', regionName: 'Europe', startsAt: '2026-05-09T20:30:00Z', visibility: 'Low but bright', score: 72, status: 'scheduled' }
  ],
  topEvents: [
    { id: 'evt-3', title: 'Meteor shower peak window', eventType: 'Meteor shower', regionName: 'Global', startsAt: '2026-05-10T04:00:00Z', priority: 98, status: 'ready' }
  ],
  pipelineRuns: [
    {
      runId: '11111111-1111-1111-1111-111111111111',
      regionId: 'na',
      regionName: 'North America',
      contentType: 'DailyGuide',
      status: 'rendering',
      runStatus: 'rendering',
      stage: 'video-render',
      startedAt: '2026-05-09T20:00:00Z',
      publishedUrls: ['https://youtube.example/watch/vid-001?sig=hidden'],
      stages: [
        { stageName: 'Script generation', status: 'completed', attemptCount: 1, maxAttempts: 3, startedUtc: '2026-05-09T20:00:00Z', completedUtc: '2026-05-09T20:03:00Z' },
        { stageName: 'Video render', status: 'running', attemptCount: 1, maxAttempts: 2, startedUtc: '2026-05-09T20:04:00Z' },
        { stageName: 'Publish', status: 'pending', maxAttempts: 3 }
      ]
    }
  ],
  settingsSummary: {
    apiBaseUrl: 'https://api.astropulse.example',
    timeoutMs: 12000,
    environment: 'production',
    productionApiConfigured: true,
    secretPolicy: 'Secret-shaped fields are stripped before rendering; tokens, app secrets, connection strings, and SAS query strings are never shown.'
  },
  warnings: []
};
