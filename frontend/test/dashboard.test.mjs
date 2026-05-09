import assert from 'node:assert/strict';
import test from 'node:test';
import { renderDashboardHtml } from '../dist/assets/ui/dashboard.js';
import { mockDashboardData } from '../dist/assets/services/mockData.js';

test('dashboard renders mock data and required sections', () => {
  const html = renderDashboardHtml(mockDashboardData);
  assert.match(html, /AstroPulse Mission Control/);
  assert.match(html, /Latest generated videos/);
  assert.match(html, /Latest shorts\/reels/);
  assert.match(html, /Platform publish status/);
  assert.match(html, /Pipeline status viewer/);
  assert.match(html, /Jupiter and the Moon/);
});
