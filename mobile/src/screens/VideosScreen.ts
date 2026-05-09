import { createCard, type MobileCard } from '../components/cards.js';
import type { MediaItem } from '../services/api.js';

export function createVideosScreen(shorts: MediaItem[]): MobileCard[] {
  return [createCard('Latest short video previews', shorts.length ? undefined : 'No shorts are available yet.', shorts.map((short) => ({
    title: short.title,
    detail: [short.regionName, short.platform, short.durationSeconds ? `${short.durationSeconds}s` : undefined].filter(Boolean).join(' • '),
    status: short.status
  })))]
}
