import type { MobileHomeData } from './api.js';

export const mockMobileHomeData: MobileHomeData = {
  regions: [
    { id: 'na', name: 'North America', timezone: 'America/New_York', language: 'en', enabled: true },
    { id: 'eu', name: 'Europe', timezone: 'Europe/London', language: 'en-GB', enabled: true },
    { id: 'pac', name: 'Pacific', timezone: 'Pacific/Auckland', language: 'en-NZ', enabled: false }
  ],
  topEvents: [
    { id: 'top-1', title: 'Meteor shower peak window', eventType: 'Meteor shower', regionName: 'Global', startsAt: '2026-05-10T04:00:00Z', visibility: 'Dark skies preferred', priority: 98, score: 98 },
    { id: 'top-2', title: 'Venus dawn elongation', eventType: 'Planet', regionName: 'Europe', startsAt: '2026-05-11T03:30:00Z', visibility: 'Clear eastern horizon', priority: 87, score: 87 }
  ],
  upcomingEvents: [
    { id: 'up-1', title: 'Moon near Regulus', eventType: 'Conjunction', regionName: 'North America', startsAt: '2026-05-10T02:00:00Z', visibility: 'Western sky after sunset', score: 72 },
    { id: 'up-2', title: 'ISS bright evening pass', eventType: 'Satellite', regionName: 'Pacific', startsAt: '2026-05-10T09:15:00Z', visibility: 'Look northwest five minutes before pass', score: 69 }
  ],
  latestShorts: [
    { id: 'short-1', title: 'Tonight: Saturn before sunrise', status: 'published', regionName: 'Pacific', platform: 'YouTube Shorts', externalUrl: 'https://youtube.com/shorts/demo?sig=hidden', durationSeconds: 42, contentType: 'short', publishedAt: '2026-05-09T08:00:00Z' },
    { id: 'reel-1', title: '60-second sky map', status: 'queued', regionName: 'Europe', platform: 'Instagram Reels', externalUrl: 'https://instagram.com/reel/demo', durationSeconds: 58, contentType: 'reel' },
    { id: 'reel-2', title: 'Meteor shower watchlist', status: 'published', regionName: 'North America', platform: 'Facebook Reels', externalUrl: 'https://facebook.com/reel/demo?token=hidden', durationSeconds: 51, contentType: 'reel', publishedAt: '2026-05-09T21:00:00Z' }
  ],
  latestVideos: [
    { id: 'yt-1', title: 'Daily Sky Guide: May 10', status: 'published', regionName: 'North America', platform: 'YouTube', externalUrl: 'https://youtube.com/watch?v=demo', durationSeconds: 420, contentType: 'daily-sky-guide', publishedAt: '2026-05-10T00:00:00Z' }
  ],
  latestPublished: { id: 'yt-1', title: 'Daily Sky Guide: May 10', status: 'published', regionName: 'North America', platform: 'YouTube', externalUrl: 'https://youtube.com/watch?v=demo', durationSeconds: 420, contentType: 'daily-sky-guide', publishedAt: '2026-05-10T00:00:00Z' },
  latestDailySkyGuide: { id: 'yt-1', title: 'Daily Sky Guide: May 10', status: 'published', regionName: 'North America', platform: 'YouTube', externalUrl: 'https://youtube.com/watch?v=demo', durationSeconds: 420, contentType: 'daily-sky-guide', publishedAt: '2026-05-10T00:00:00Z' },
  pipelineRuns: [
    { runId: 'run-20260510-na', status: 'completed', stage: 'published', updatedAt: '2026-05-10T00:10:00Z', platforms: [{ platform: 'YouTube', status: 'published', publishedAt: '2026-05-10T00:00:00Z' }] },
    { runId: 'run-20260510-eu', status: 'rendering', stage: 'video-render', updatedAt: '2026-05-10T00:30:00Z' }
  ],
  platformStatuses: [
    { platform: 'YouTube', status: 'published', publishedAt: '2026-05-10T00:00:00Z', itemId: 'yt-1' },
    { platform: 'Instagram Reels', status: 'queued', itemId: 'reel-1' },
    { platform: 'Facebook Reels', status: 'published', publishedAt: '2026-05-09T21:00:00Z', itemId: 'reel-2' }
  ],
  scheduler: { isEnabled: true, state: 'running', nextRunAt: '2026-05-10T22:00:00Z', lastRunAt: '2026-05-10T00:00:00Z' },
  analytics: { views: 124800, watchTimeMinutes: 9200, engagementRate: 6.4, topPlatform: 'YouTube' },
  tokenHealth: [
    { provider: 'YouTube', status: 'healthy' },
    { provider: 'Instagram', status: 'warning', expiresAt: '2026-05-12T00:00:00Z' }
  ],
  alertUpcomingEvents: [
    { id: 'alert-1', title: 'Meteor shower email alert candidate', eventType: 'MeteorShower', regionName: 'Global', startsAt: '2026-05-10T04:00:00Z', visibility: 'Email-ready backend candidate', score: 0.82 }
  ],
  developmentMockData: true
};
