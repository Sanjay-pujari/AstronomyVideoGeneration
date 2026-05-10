import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import { createSafeExternalLink, type MediaItem, type MobileHomeData } from '../services/api.js';

function rowsFor(items: MediaItem[]) {
  return items.map((item) => ({
    title: item.title,
    detail: [item.regionName, item.durationSeconds ? `${item.durationSeconds}s` : undefined, item.publishedAt].filter(Boolean).join(' • '),
    href: createSafeExternalLink(item.externalUrl ?? item.previewUrl),
    status: item.status
  }));
}

export function createVideosScreen(data: Pick<MobileHomeData, 'latestVideos' | 'latestShorts'>): MobileScreenModel {
  const longVideos = data.latestVideos.filter((item) => item.platform === 'YouTube' || item.contentType === 'long-video' || item.contentType === 'daily-sky-guide');
  const youtubeShorts = data.latestShorts.filter((item) => item.platform === 'YouTube Shorts' || item.platform === 'YouTube');
  const facebookReels = data.latestShorts.filter((item) => item.platform === 'Facebook Reels' || item.platform === 'Facebook');
  const instagramReels = data.latestShorts.filter((item) => item.platform === 'Instagram Reels' || item.platform === 'Instagram');

  return createScreen('Videos', 'Safe external links are sanitized before display', [
    createCard('Latest YouTube long videos', longVideos.length ? undefined : 'No YouTube long videos are available yet.', rowsFor(longVideos), longVideos.length ? 'idle' : 'empty', 'video'),
    createCard('YouTube Shorts', youtubeShorts.length ? undefined : 'No YouTube Shorts are available yet.', rowsFor(youtubeShorts), youtubeShorts.length ? 'idle' : 'empty', 'video'),
    createCard('Facebook Reels', facebookReels.length ? undefined : 'No Facebook Reels are available yet.', rowsFor(facebookReels), facebookReels.length ? 'idle' : 'empty', 'video'),
    createCard('Instagram Reels', instagramReels.length ? undefined : 'No Instagram Reels are available yet.', rowsFor(instagramReels), instagramReels.length ? 'idle' : 'empty', 'video')
  ]);
}
