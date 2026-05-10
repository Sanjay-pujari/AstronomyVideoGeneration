import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import { createSafeExternalLink, type MobileHomeData } from '../services/api.js';

export function createSkyScreen(data: MobileHomeData, selectedRegionId?: string): MobileScreenModel {
  const selectedRegion = data.regions.find((region) => region.id === selectedRegionId) ?? data.regions[0];
  const regionEvents = data.upcomingEvents.filter((event) => !selectedRegion || event.regionName === selectedRegion.name || event.regionName === 'Global');
  const dailyGuide = data.latestDailySkyGuide ?? data.latestVideos.find((video) => video.contentType === 'daily-sky-guide') ?? data.latestVideos[0];

  return createScreen('Tonight’s Sky', selectedRegion ? selectedRegion.name : 'Select a region', [
    createCard('Selected region', selectedRegion ? `${selectedRegion.timezone ?? 'local time'} • ${selectedRegion.language ?? 'default language'}` : 'Choose a region to personalize sky visibility.', selectedRegion ? [{ title: selectedRegion.name, status: selectedRegion.enabled === false ? 'disabled' : 'enabled' }] : [], selectedRegion ? 'idle' : 'empty', 'sky'),
    createCard('Visible objects summary', regionEvents.length ? 'Based on upcoming event visibility summaries.' : 'No visible object summaries are available for this region yet.', regionEvents.map((event) => ({
      title: event.title,
      detail: event.visibility ?? event.startsAt,
      badge: event.eventType,
      status: String(event.score ?? event.priority ?? 'visible')
    })), regionEvents.length ? 'idle' : 'empty', 'sky'),
    createCard('Latest DailySkyGuide video', dailyGuide ? undefined : 'DailySkyGuide video metadata is not available yet.', dailyGuide ? [{
      title: dailyGuide.title,
      detail: [dailyGuide.platform, dailyGuide.publishedAt].filter(Boolean).join(' • '),
      href: createSafeExternalLink(dailyGuide.externalUrl),
      status: dailyGuide.status
    }] : [], dailyGuide ? 'idle' : 'empty', 'video'),
    createCard('Latest short/reel previews', data.latestShorts.length ? undefined : 'No short-form previews are available yet.', data.latestShorts.map((short) => ({
      title: short.title,
      detail: [short.platform, short.regionName, short.durationSeconds ? `${short.durationSeconds}s` : undefined].filter(Boolean).join(' • '),
      href: createSafeExternalLink(short.externalUrl),
      status: short.status
    })), data.latestShorts.length ? 'idle' : 'empty', 'video')
  ]);
}
