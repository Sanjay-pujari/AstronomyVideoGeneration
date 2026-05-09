import { createCard, type MobileCard } from '../components/cards.js';
import type { AstroEvent } from '../services/api.js';

export function createAlertsScreen(events: AstroEvent[]): MobileCard[] {
  return [createCard('Event alerts', 'Notification-ready alert structure for future push opt-in.', events.map((event) => ({
    title: event.title,
    detail: [event.regionName, event.eventType, event.startsAt].filter(Boolean).join(' • '),
    status: event.priority ? String(event.priority) : 'soon'
  })))];
}
