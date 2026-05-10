import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

const EVENT_TYPES = ['Visible planets', 'Full moon / supermoon', 'Meteor showers', 'Eclipses', 'Special event videos', 'Daily sky guide reminders'];

export function createAlertPreferencesScreen(data: MobileHomeData, selectedRegionId?: string): MobileScreenModel {
  const selectedRegion = data.regions.find((region) => region.id === selectedRegionId) ?? data.regions[0];

  return createScreen('Alert preferences', 'Local-only preference foundation; backend alert APIs are not available yet.', [
    createCard('Region and language', 'Choose where and how sky alerts should be localized.', [
      { title: selectedRegion?.name ?? 'No region selected', detail: [selectedRegion?.timezone, selectedRegion?.language].filter(Boolean).join(' • '), status: selectedRegion ? 'selected' : 'needed' }
    ], selectedRegion ? 'idle' : 'empty', 'sky'),
    createCard('Event type toggles', 'Notification-ready toggles for future backend preferences.', EVENT_TYPES.map((type) => ({
      title: type,
      detail: type === 'Daily sky guide reminders' ? 'Default reminder candidate' : 'Optional sky alert category',
      status: 'notification-ready'
    })), 'idle', 'event'),
    createCard('Preferred alert time', 'Local device reminder placeholder only.', [
      { title: '19:30 local time', detail: 'Stored in the screen model only until preferences APIs exist.', status: 'local placeholder' }
    ], 'idle', 'muted'),
    createCard('Channels', 'No real push token, email subscription, or WhatsApp registration is implemented.', [
      { title: 'Email', status: 'coming soon' },
      { title: 'Push', detail: 'Local notification placeholder only', status: 'notification-ready' },
      { title: 'WhatsApp', status: 'optional later' }
    ], 'idle', 'brand')
  ]);
}
