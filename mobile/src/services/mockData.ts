import type { MobileHomeData } from './api.js';

export const mockMobileHomeData: MobileHomeData = {
  regions: [
    { id: 'na', name: 'North America', timezone: 'America/New_York', enabled: true },
    { id: 'eu', name: 'Europe', timezone: 'Europe/London', enabled: true },
    { id: 'pac', name: 'Pacific', timezone: 'Pacific/Auckland', enabled: false }
  ],
  topEvents: [
    { id: 'top-1', title: 'Meteor shower peak window', eventType: 'Meteor shower', regionName: 'Global', startsAt: '2026-05-10T04:00:00Z', visibility: 'Dark skies preferred', priority: 98 }
  ],
  upcomingEvents: [
    { id: 'up-1', title: 'Moon near Regulus', eventType: 'Conjunction', regionName: 'North America', startsAt: '2026-05-09T02:00:00Z', visibility: 'Western sky after sunset' }
  ],
  latestShorts: [
    { id: 'short-1', title: 'Tonight: Saturn before sunrise', status: 'published', regionName: 'Pacific', platform: 'TikTok', durationSeconds: 42 },
    { id: 'short-2', title: '60-second sky map', status: 'queued', regionName: 'Europe', platform: 'Instagram', durationSeconds: 58 }
  ],
  scheduler: { isEnabled: true, state: 'running', nextRunAt: '2026-05-09T22:00:00Z' },
  analytics: { views: 124800, watchTimeMinutes: 9200, engagementRate: 6.4, topPlatform: 'YouTube' },
  tokenHealth: [
    { provider: 'YouTube', status: 'healthy' },
    { provider: 'TikTok', status: 'warning', expiresAt: '2026-05-12T00:00:00Z' }
  ]
};
