import assert from 'node:assert/strict';
import test from 'node:test';
import { renderDashboardHtml } from '../dist/assets/ui/dashboard.js';
import { mockDashboardData } from '../dist/assets/services/mockData.js';

test('dashboard renders mock data and required sections', () => {
  const html = renderDashboardHtml(mockDashboardData);
  assert.match(html, /AstroPulse Mission Control/);
  assert.match(html, /Overall system health/);
  assert.match(html, /Latest generated videos/);
  assert.match(html, /Latest shorts\/reels/);
  assert.match(html, /Platform publish status/);
  assert.match(html, /Token health/);
  assert.match(html, /Scheduler status/);
  assert.match(html, /Jupiter and the Moon/);
});

test('pipeline runs page renders details, timeline, and published URLs', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'runs' });
  assert.match(html, /Recent pipeline runs/);
  assert.match(html, /Run details/);
  assert.match(html, /Stage timeline/);
  assert.match(html, /Published URLs/);
});

test('API errors render friendly fallback message', () => {
  const html = renderDashboardHtml(mockDashboardData, { error: 'offline', page: 'dashboard' });
  assert.match(html, /Signal lost/);
  assert.match(html, /Showing safe mock telemetry/);
});

test('secret fields and SAS query strings are not rendered', () => {
  const html = renderDashboardHtml({
    ...mockDashboardData,
    tokenHealth: [{ provider: 'Provider', status: 'healthy', message: 'visible', accessToken: 'hidden-token' }],
    publishStatuses: [{ platform: 'Blob', status: 'published', url: 'https://storage.example/video.mp4?sig=hidden&sv=secret' }]
  });
  assert.doesNotMatch(html, /hidden-token/);
  assert.doesNotMatch(html, /sig=hidden/);
  assert.doesNotMatch(html, /sv=secret/);
  assert.match(html, /https:\/\/storage.example\/video.mp4/);
});
