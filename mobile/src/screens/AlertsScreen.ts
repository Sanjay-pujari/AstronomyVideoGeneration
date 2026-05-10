import { createCard, createScreen, type MobileScreenModel } from '../components/cards.js';
import type { MobileHomeData } from '../services/api.js';

function classifyEvent(title: string, type?: string) {
  const value = `${title} ${type ?? ''}`.toLowerCase();
  if (value.includes('planet') || value.includes('venus') || value.includes('mars') || value.includes('jupiter') || value.includes('saturn')) return 'Visible planet';
  if (value.includes('moon') || value.includes('supermoon')) return 'Full moon / supermoon';
  if (value.includes('meteor')) return 'Meteor shower';
  if (value.includes('eclipse')) return 'Eclipse';
  return type ?? 'Sky event';
}

export function createAlertsScreen(data: MobileHomeData): MobileScreenModel {
  const events = [...data.upcomingEvents, ...data.topEvents].filter((event, index, all) => all.findIndex((item) => item.id === event.id) === index);
  return createScreen('Sky alerts', 'Friendly notification-ready foundation with local placeholders only.', [
    createCard('Upcoming sky alerts', 'Notify-me rows are local placeholders until alert APIs launch.', events.map((event) => ({
      title: event.title,
      detail: [event.regionName, event.eventType, event.startsAt].filter(Boolean).join(' • '),
      badge: classifyEvent(event.title, event.eventType),
      status: 'notify-ready'
    })), events.length ? 'idle' : 'empty', 'event'),
    createCard('Local notification placeholder', 'No push token is registered and no production alert subscription is created.', [
      { title: 'Daily sky guide reminder', detail: '19:30 local time placeholder', status: 'notification-ready' },
      { title: 'Special event video', detail: 'Remind me when a related video is available', status: 'notification-ready' },
      { title: 'Backend actions', detail: 'POST /api/alerts/subscribe is unavailable', status: 'unavailable' }
    ], 'idle', 'brand')
  ]);
}
