import { createCard, type MobileCard } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

export function createSettingsScreen(data: MobileHomeData, selectedRegionId?: string): MobileCard[] {
  return [
    createCard('Region selector', selectedRegionId ?? data.regions[0]?.id, data.regions.map((region) => ({ title: region.name, detail: region.timezone, status: region.enabled === false ? 'disabled' : 'enabled' }))),
    createCard('Notifications', 'Push notifications are scaffolded for event alerts; provider wiring can be added later without exposing tokens.', [{ title: 'Event alerts', status: 'ready' }]),
    createCard('Token health', 'Provider health only; no secrets displayed.', data.tokenHealth.map((token) => ({ title: token.provider, detail: token.expiresAt ?? token.message, status: token.status })))
  ];
}
