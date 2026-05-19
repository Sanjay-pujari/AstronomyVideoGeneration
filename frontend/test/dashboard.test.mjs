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
  assert.match(html, /Engagement/);
  assert.match(html, /8,200/);
  assert.match(html, /YouTube/);
  assert.match(html, /Facebook/);
  assert.match(html, /Instagram/);
  assert.match(html, /Jupiter and the Moon/);
});

test('dashboard operations page renders latest published content from analytics data', () => {
  const html = renderDashboardHtml(mockDashboardData);
  assert.match(html, /Overall system health/);
  assert.match(html, /Latest pipeline runs/);
  assert.match(html, /AI recommendations summary/);
  assert.match(html, /Platform publish status/);
  assert.match(html, /Failed stages/);
  assert.match(html, /Published/);
});

test('empty analytics shows empty onboarding state instead of fake values', () => {
  const html = renderDashboardHtml(emptyDashboardData(), { page: 'analytics' });
  assert.match(html, /Waiting for analytics data/);
  assert.match(html, /Waiting for analytics data/);
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
  const html = renderDashboardHtml(mockDashboardData, { page: 'pipeline-runs' });
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


test('admin alerts page renders backend alert subscription controls', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'alerts' });
  assert.match(html, /Sky alert subscription API/);
  assert.match(html, /Save alert subscription/);
  assert.match(html, /Send test alert/);
  assert.match(html, /Unsubscribe/);
  assert.match(html, /POST \/api\/alerts\/subscribe is ready/);
  assert.match(html, /Upcoming alert candidates/);
});


test('AI optimization and optimization insights pages render', () => {
  const aiHtml = renderDashboardHtml(mockDashboardData, { page: 'ai-optimization' });
  const insightsHtml = renderDashboardHtml(mockDashboardData, { page: 'optimization-insights' });
  assert.match(aiHtml, /Hook scores and recommendations/);
  assert.match(insightsHtml, /Optimization insights/);
});

test('dashboard quick links use hash href values', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'dashboard' });
  assert.match(html, /href="#analytics"/);
  assert.match(html, /href="#ai-optimization"/);
  assert.match(html, /href="#optimization-insights"/);
  assert.match(html, /href="#pipeline-runs"/);
});

test('warning renders for unknown dashboard pages', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'dashboard', warning: 'Unknown dashboard page: bogus' });
  assert.match(html, /Unknown dashboard page: bogus/);
});
