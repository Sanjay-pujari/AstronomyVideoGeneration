import assert from 'node:assert/strict';
import test from 'node:test';
import { createMobileAppModel } from '../.test-build/App.js';
import { mockMobileHomeData } from '../.test-build/services/mockData.js';

test('mobile home model renders with mock sky data', () => {
  const model = createMobileAppModel(mockMobileHomeData, 'home');
  assert.equal(model.brand, 'AstroPulse');
  assert.equal(model.navigation.notificationReady, true);
  assert.equal(model.screen[0].title, 'AstroPulse Home');
  assert.equal(model.screen[1].title, 'Tonight’s sky summary');
});

test('mobile settings model includes region selector and notification-ready card', () => {
  const model = createMobileAppModel(mockMobileHomeData, 'settings', 'eu');
  assert.equal(model.screen[0].title, 'Region selector');
  assert.match(JSON.stringify(model), /Notifications/);
});
