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
  const events = [...(data.alertUpcomingEvents.length ? data.alertUpcomingEvents : data.upcomingEvents), ...data.topEvents].filter((event, index, all) => all.findIndex((item) => item.id === event.id) === index);
  return createScreen('Sky alerts', 'Email sky-alert subscriptions are backed by the AstroPulse alert APIs.', [
    createCard('Upcoming sky alerts', 'Upcoming candidates are loaded from /api/alerts/upcoming when available.', events.map((event) => ({
      title: event.title,
      detail: [event.regionName, event.eventType, event.startsAt].filter(Boolean).join(' • '),
      badge: classifyEvent(event.title, event.eventType),
      status: 'notify-ready'
    })), events.length ? 'idle' : 'empty', 'event'),
    createCard('Alert API actions', 'Subscribe, test-alert, preferences, and unsubscribe calls are available through the mobile API service.', [
      { title: 'Daily sky guide reminder', detail: '19:30 local time placeholder', status: 'notification-ready' },
      { title: 'Special event video', detail: 'Remind me when a related video is available', status: 'notification-ready' },
      { title: 'Backend actions', detail: 'POST /api/alerts/subscribe and POST /api/alerts/test are wired', status: 'api-ready' }
    ], 'idle', 'brand')
  ]);
}
