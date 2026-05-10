import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

const EVENT_TYPES = ['Visible planets', 'Full moon / supermoon', 'Meteor showers', 'Eclipses', 'Special event videos', 'Daily sky guide reminders'];

export function createAlertPreferencesScreen(data: MobileHomeData, selectedRegionId?: string): MobileScreenModel {
  const selectedRegion = data.regions.find((region) => region.id === selectedRegionId) ?? data.regions[0];

  return createScreen('Alert preferences', 'Preference screen wired to backend alert subscribe and preferences APIs.', [
    createCard('Region and language', 'Choose where and how sky alerts should be localized.', [
      { title: selectedRegion?.name ?? 'No region selected', detail: [selectedRegion?.timezone, selectedRegion?.language].filter(Boolean).join(' • '), status: selectedRegion ? 'selected' : 'needed' }
    ], selectedRegion ? 'idle' : 'empty', 'sky'),
    createCard('Event type toggles', 'Toggles map to backend eventTypes values for email alerts.', EVENT_TYPES.map((type) => ({
      title: type,
      detail: type === 'Daily sky guide reminders' ? 'Default reminder candidate' : 'Optional sky alert category',
      status: 'notification-ready'
    })), 'idle', 'event'),
    createCard('Preferred alert time', 'Preferred local time is sent as preferredAlertTimeLocal.', [
      { title: '19:30 local time', detail: 'Persisted through PUT /api/alerts/preferences/{subscriberId}.', status: 'local placeholder' }
    ], 'idle', 'muted'),
    createCard('Channels', 'Email channel is available now; push and WhatsApp remain future options.', [
      { title: 'Email', status: 'api-ready' },
      { title: 'Push', detail: 'Local notification placeholder only', status: 'notification-ready' },
      { title: 'WhatsApp', status: 'optional later' }
    ], 'idle', 'brand')
  ]);
}
