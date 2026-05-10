import { API_BASE_URL, MOCK_MODE_ENABLED } from '../config/environment.js';
import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

export function createSettingsScreen(data: MobileHomeData, selectedRegionId?: string): MobileScreenModel {
  const selectedRegion = data.regions.find((region) => region.id === selectedRegionId) ?? data.regions[0];

  return createScreen('Settings', 'Local preferences and notification-ready controls', [
    createCard('API base URL', API_BASE_URL, [
      { title: 'Timeout and friendly error handling', detail: 'Configured in the central API client', status: 'enabled' },
      { title: 'Mock mode', detail: MOCK_MODE_ENABLED ? 'Development only mock data may be shown' : 'Disabled', status: MOCK_MODE_ENABLED ? 'development only' : 'off' }
    ], 'idle', 'muted'),
    createCard('Selected region', selectedRegion?.id ?? 'No selected region', data.regions.map((region) => ({
      title: region.name,
      detail: [region.timezone, region.language].filter(Boolean).join(' • '),
      status: region.id === selectedRegion?.id ? 'selected' : region.enabled === false ? 'disabled' : 'available'
    })), data.regions.length ? 'idle' : 'empty', 'muted'),
    createCard('Language', selectedRegion?.language ?? 'System default', [
      { title: 'Content language', detail: 'Stored as a UI preference foundation; backend localization can be wired later.', status: selectedRegion?.language ?? 'default' }
    ], 'idle', 'muted'),
    createCard('Notifications', 'Push notifications are scaffolded only; no provider or real push implementation is wired.', [
      { title: 'Event alerts', status: 'notification-ready' },
      { title: 'Publishing failures', status: 'notification-ready' },
      { title: 'Daily sky reminder', status: 'notification-ready' }
    ], 'idle', 'brand'),
    createCard('Admin Lite access', 'Open System / Admin Lite from settings for scheduler, token health, pipeline, and publish status cards.', [
      { title: 'System / Admin Lite', status: 'available' },
      { title: 'Regions', status: 'available' }
    ], 'idle', 'system')
  ]);
}
