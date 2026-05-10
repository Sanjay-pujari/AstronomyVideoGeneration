import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { Region } from '../services/api.js';

export function createRegionsScreen(regions: Region[]): MobileScreenModel {
  return createScreen('Regions', 'Regional publishing configuration', [
    createCard('Region list', regions.length ? undefined : 'No regions are configured yet.', regions.map((region) => ({
      title: region.name,
      detail: [region.timezone ?? 'timezone pending', region.language ?? 'default language'].join(' • '),
      status: region.enabled === false ? 'disabled' : 'enabled'
    })), regions.length ? 'idle' : 'empty', 'muted')
  ]);
}
