import { createCard, type MobileCard } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

export function createHomeScreen(data: MobileHomeData, selectedRegionId?: string): MobileCard[] {
  const selectedRegion = data.regions.find((region) => region.id === selectedRegionId) ?? data.regions[0];
  const tonight = data.upcomingEvents[0] ?? data.topEvents[0];
  return [
    createCard('AstroPulse Home', selectedRegion ? `${selectedRegion.name} • ${selectedRegion.timezone ?? 'local time'}` : 'Choose a region'),
    createCard('Tonight’s sky summary', tonight?.visibility ?? 'Visibility guidance will appear when event data is available.', [
      { title: tonight?.title ?? 'No highlighted event yet', detail: tonight ? `${tonight.eventType ?? 'Sky event'} • ${tonight.startsAt}` : 'Check back after scheduler sync' }
    ]),
    createCard('Operations pulse', `Scheduler ${data.scheduler.state}`, [
      { title: data.analytics.topPlatform ?? 'All platforms', detail: `${(data.analytics.views ?? 0).toLocaleString()} recent views`, status: 'live' }
    ])
  ];
}
