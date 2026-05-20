import { api, loadPublicPortalData } from '../services/api.js';

export const publicApi = {
  loadPublicPortalData,
  getRegions: api.getRegions,
  getUpcomingEvents: api.getUpcomingEvents,
  getTopEvents: api.getTopEvents,
    subscribeToAlerts: api.subscribeToAlerts,
  getUpcomingAlerts: api.getUpcomingAlerts,
  updateAlertPreferences: api.updateAlertPreferences,
  sendTestAlert: api.sendTestAlert,
  unsubscribeAlerts: api.unsubscribeAlerts
};
