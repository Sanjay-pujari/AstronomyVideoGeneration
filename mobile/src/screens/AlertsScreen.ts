import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { AstroEvent } from '../services/api.js';

export function createAlertsScreen(events: AstroEvent[]): MobileScreenModel {
  return createScreen('Event alerts', 'Notification-ready alert structure for future push opt-in.', [
    createCard('Event alerts', undefined, events.map((event) => ({
      title: event.title,
      detail: [event.regionName, event.eventType, event.startsAt].filter(Boolean).join(' • '),
      badge: event.eventType,
      status: event.priority || event.score ? String(event.priority ?? event.score) : 'soon'
    })), events.length ? 'idle' : 'empty', 'event')
  ]);
}
