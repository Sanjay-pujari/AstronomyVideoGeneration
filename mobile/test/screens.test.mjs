import assert from 'node:assert/strict';
import test from 'node:test';
import { createMobileAppModel } from '../.test-build/App.js';
import { mockMobileHomeData } from '../.test-build/services/mockData.js';

function cardTitles(model) {
  return model.screen.cards.map((card) => card.title);
}

test('mobile home model renders AstroPulse branding with mock sky data', () => {
  const model = createMobileAppModel(mockMobileHomeData, 'home');
  assert.equal(model.brand, 'AstroPulse');
  assert.equal(model.theme.mode, 'dark');
  assert.equal(model.navigation.notificationReady, true);
  assert.deepEqual(model.navigation.bottomTabs.map((tab) => tab.id), ['home', 'sky', 'events', 'alerts', 'videos', 'settings']);
  assert.deepEqual(cardTitles(model), ['AstroPulse Home', 'Tonight’s sky summary', 'Latest published video/reel', 'Quick event alert', 'Region selector']);
  assert.equal(model.screen.supportsPullToRefresh, true);
});

test('sky, events, alerts, videos, and settings screens render with mock data', () => {
  assert.match(cardTitles(createMobileAppModel(mockMobileHomeData, 'sky', 'eu')).join('|'), /Latest DailySkyGuide video/);
  assert.match(cardTitles(createMobileAppModel(mockMobileHomeData, 'events')).join('|'), /Upcoming events\|Top events/);
  assert.match(cardTitles(createMobileAppModel(mockMobileHomeData, 'alerts')).join('|'), /Upcoming sky alerts\|Local notification placeholder/);
  assert.match(cardTitles(createMobileAppModel(mockMobileHomeData, 'videos')).join('|'), /Latest YouTube long videos\|YouTube Shorts\|Facebook Reels\|Instagram Reels/);
  assert.match(cardTitles(createMobileAppModel(mockMobileHomeData, 'settings')).join('|'), /API base URL\|Selected region\|Language\|Notifications/);
});

test('secondary region and system admin lite screens are available from settings', () => {
  const model = createMobileAppModel(mockMobileHomeData, 'settings', 'eu');
  assert.equal(model.secondaryScreens.regions.cards[0].title, 'Region list');
  assert.match(model.secondaryScreens.alertPreferences.cards.map((card) => card.title).join('|'), /Region and language\|Event type toggles\|Preferred alert time\|Channels/);
  assert.match(model.secondaryScreens.systemAdminLite.cards.map((card) => card.title).join('|'), /Scheduler status\|Token health\|Latest pipeline runs\|Platform publish status/);
});

test('screen models never expose secret-shaped mock fields', () => {
  const unsafe = structuredClone(mockMobileHomeData);
  unsafe.tokenHealth[0].accessToken = 'hidden';
  const model = createMobileAppModel(unsafe, 'settings');
  assert.doesNotMatch(JSON.stringify(model), /hidden|accessToken|refreshToken|connectionString|sasToken/i);
});
