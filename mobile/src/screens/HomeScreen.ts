import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import { createSafeExternalLink, type MobileHomeData } from '../services/api.js';

export function createHomeScreen(data: MobileHomeData, selectedRegionId?: string): MobileScreenModel {
  const selectedRegion = data.regions.find((region) => region.id === selectedRegionId) ?? data.regions[0];
  const tonight = data.upcomingEvents.find((event) => !selectedRegion || event.regionName === selectedRegion.name || event.regionName === 'Global') ?? data.upcomingEvents[0] ?? data.topEvents[0];
  const latest = data.latestPublished ?? data.latestDailySkyGuide ?? data.latestVideos[0] ?? data.latestShorts[0];
  const alert = data.topEvents[0] ?? data.upcomingEvents[0];

  return createScreen('AstroPulse', data.developmentMockData ? 'Development mock data only' : 'Astronomy content and platform pulse', [
    createCard('AstroPulse Home', selectedRegion ? `${selectedRegion.name} • ${selectedRegion.timezone ?? 'local time'}` : 'Choose a region', [
      { title: 'Dark astronomy theme', detail: 'Mobile-first cards with pull-to-refresh-ready sections', status: 'ready' }
    ], 'idle', 'brand'),
    createCard('Tonight’s sky summary', tonight?.visibility ?? 'Visibility guidance will appear when event data is available.', [
      { title: tonight?.title ?? 'No highlighted event yet', detail: tonight ? `${tonight.eventType ?? 'Sky event'} • ${tonight.startsAt}` : 'Check back after scheduler sync', badge: tonight?.eventType }
    ], tonight ? 'idle' : 'empty', 'sky'),
    createCard('Latest published video/reel', latest ? undefined : 'No published media is available yet.', latest ? [{
      title: latest.title,
      detail: [latest.platform, latest.regionName, latest.publishedAt].filter(Boolean).join(' • '),
      href: createSafeExternalLink(latest.externalUrl),
      status: latest.status ?? 'available'
    }] : [], latest ? 'idle' : 'empty', 'video'),
    createCard('Quick event alert', 'Notification-ready alert card; no push provider is wired yet.', alert ? [{
      title: alert.title,
      detail: [alert.regionName, alert.startsAt].filter(Boolean).join(' • '),
      badge: alert.eventType,
      status: String(alert.score ?? alert.priority ?? 'soon')
    }] : [], alert ? 'idle' : 'empty', 'event'),
    createCard('Region selector', selectedRegion?.id ?? 'No region selected', data.regions.map((region) => ({
      title: region.name,
      detail: [region.timezone, region.language].filter(Boolean).join(' • '),
      status: region.enabled === false ? 'disabled' : 'enabled'
    })), data.regions.length ? 'idle' : 'empty', 'muted')
  ]);
}
