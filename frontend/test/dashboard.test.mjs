import assert from 'node:assert/strict';
import test from 'node:test';
import { emptyDashboardData } from '../dist/assets/services/api.js';
import { renderDashboardHtml } from '../dist/assets/ui/dashboard.js';
import { mockDashboardData } from '../dist/assets/services/mockData.js';

test('dashboard renders real analytics sections and required operations widgets', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'analytics' });
  assert.match(html, /AstroPulse Mission Control/);
  assert.match(html, /Total views/);
  assert.match(html, /124,800/);
  assert.match(html, /Total engagement/);
  assert.match(html, /8,200/);
  assert.match(html, /YouTube/);
  assert.match(html, /Facebook/);
  assert.match(html, /Instagram/);
  assert.match(html, /Jupiter and the Moon/);
});

test('dashboard operations page renders latest published content from analytics data', () => {
  const html = renderDashboardHtml(mockDashboardData);
  assert.match(html, /Overall system health/);
  assert.match(html, /Latest generated videos/);
  assert.match(html, /Latest shorts\/reels/);
  assert.match(html, /Platform publish status/);
  assert.match(html, /Token health/);
  assert.match(html, /Scheduler status/);
  assert.match(html, /Published/);
  assert.match(html, /views/);
});

test('empty analytics shows empty onboarding state instead of fake values', () => {
  const html = renderDashboardHtml(emptyDashboardData(), { page: 'analytics' });
  assert.match(html, /Waiting for analytics data/);
  assert.match(html, /Connect YouTube, Facebook, or Instagram analytics collection/);
  assert.doesNotMatch(html, /124,800/);
  assert.doesNotMatch(html, /8,200/);
});

test('TikTok platform placeholders are removed', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'analytics' });
  assert.doesNotMatch(html, /TikTok/i);
});

test('API errors render clean service-unavailable message', () => {
  const html = renderDashboardHtml(emptyDashboardData(), { error: 'Waiting for analytics data.', page: 'dashboard' });
  assert.match(html, /Analytics service temporarily unavailable/);
  assert.match(html, /Waiting for analytics data/);
  assert.doesNotMatch(html, /Signal lost/);
  assert.doesNotMatch(html, /Showing safe mock telemetry/);
});

test('failed API state does not render fake metrics', () => {
  const html = renderDashboardHtml(emptyDashboardData(), { error: 'Analytics service temporarily unavailable.', page: 'analytics' });
  assert.match(html, /Total views/);
  assert.match(html, /<strong>—<\/strong>/);
  assert.doesNotMatch(html, /124,800/);
  assert.doesNotMatch(html, /82,000|8,200/);
});

test('pipeline runs page renders details, timeline, and published URLs', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'runs' });
  assert.match(html, /Recent pipeline runs/);
  assert.match(html, /Run details/);
  assert.match(html, /Stage timeline/);
  assert.match(html, /Published URLs/);
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


test('admin alerts preview renders missing backend warning and candidate placeholders', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'alerts' });
  assert.match(html, /Missing alert API warning/);
  assert.match(html, /Alert queue mock/);
  assert.match(html, /Upcoming alert candidates/);
  assert.match(html, /POST \/api\/alerts\/subscribe/);
  assert.match(html, /No production subscriptions or test notifications/);
});
