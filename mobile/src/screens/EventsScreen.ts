import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

export function createEventsScreen(data: MobileHomeData): MobileScreenModel {
  return createScreen('Events', 'Upcoming and top astronomy event cards', [
    createCard('Upcoming events', data.upcomingEvents.length ? undefined : 'No upcoming events returned by the API.', data.upcomingEvents.map((event) => ({
      title: event.title,
      detail: [event.regionName, event.startsAt, event.visibility].filter(Boolean).join(' • '),
      badge: event.eventType,
      status: `score ${event.score ?? event.priority ?? 'n/a'}`
    })), data.upcomingEvents.length ? 'idle' : 'empty', 'event'),
    createCard('Top events', data.topEvents.length ? undefined : 'No top events returned by the API.', data.topEvents.map((event) => ({
      title: event.title,
      detail: [event.regionName, event.startsAt, event.visibility].filter(Boolean).join(' • '),
      badge: event.eventType,
      status: `score ${event.score ?? event.priority ?? 'n/a'}`
    })), data.topEvents.length ? 'idle' : 'empty', 'event')
  ]);
}
