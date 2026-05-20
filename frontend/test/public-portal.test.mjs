import assert from 'node:assert/strict';
import test from 'node:test';
import { mockDashboardData } from '../dist/assets/services/mockData.js';
import { emptyDashboardData, loadPublicPortalData } from '../dist/assets/services/api.js';
import { parsePublicRoute, renderPublicPortalHtml } from '../dist/assets/ui/publicPortal.js';
import { renderDashboardHtml } from '../dist/assets/ui/dashboard.js';

const json = (body) => new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });

test('public home renders AstroPulse portal content and social CTAs', () => {
  const html = renderPublicPortalHtml(mockDashboardData, { page: 'home' });
  assert.match(html, /Explore tonight&#39;s sky with AstroPulse|Explore tonight's sky with AstroPulse/);
  assert.match(html, /Latest sky guides/);
  assert.match(html, /Latest shorts and reels/);
  assert.match(html, /Upcoming sky events/);
  assert.match(html, /Follow on YouTube/);
  assert.doesNotMatch(html, /AI Optimization|Analytics|Pipeline Runs|Settings/);
  assert.match(html, /Choose your region/);
});

test('admin dashboard still renders at admin route target', () => {
  const html = renderDashboardHtml(mockDashboardData, { page: 'dashboard' });
  assert.match(html, /AstroPulse Mission Control/);
  assert.match(html, /Overall system health/);
});

test('public pages do not show internal run or stage data', () => {
  const html = renderPublicPortalHtml(mockDashboardData, { page: 'home' });
  assert.doesNotMatch(html, /Pipeline Runs/);
  assert.doesNotMatch(html, /Stage timeline/);
  assert.doesNotMatch(html, /11111111-1111-1111-1111-111111111111/);
  assert.doesNotMatch(html, /video-render/);
  assert.doesNotMatch(html, /sig=hidden|sv=sas-token/);
});

test('public empty states do not render mock metrics', () => {
  const html = renderPublicPortalHtml(emptyDashboardData(), { page: 'videos' });
  assert.match(html, /Long-form videos are coming soon/);
  assert.doesNotMatch(html, /124,800/);
  assert.doesNotMatch(html, /8,200/);
});

test('region pages route correctly and include locale details', () => {
  assert.deepEqual(parsePublicRoute('/regions/na'), { page: 'region', regionId: 'na' });
  assert.deepEqual(parsePublicRoute('/tonights-sky'), { page: 'tonight' });
  const html = renderPublicPortalHtml(mockDashboardData, parsePublicRoute('/regions/na'));
  assert.match(html, /North America/);
  assert.match(html, /America\/New_York/);
  assert.match(html, /Language: en/);
});

test('loadPublicPortalData avoids operations APIs', async () => {
  const paths = [];
  globalThis.fetch = async (url) => {
    const path = new URL(url).pathname;
    paths.push(path);
    if (path === '/api/regions') return json([{ id: 'na', name: 'North America', timezone: 'America/New_York', language: 'en' }]);
    if (path === '/api/events/upcoming') return json([]);
    if (path === '/api/events/top') return json([]);
    if (path === '/api/analytics/dashboard') return json({ overallSummary: { totalViews: 0 }, platformBreakdown: [] });
    if (path === '/api/analytics/top-content') return json([]);
    return json({});
  };

  const data = await loadPublicPortalData();
  assert.equal(data.regions.length, 1);
  assert.ok(!paths.includes('/api/ops/dashboard'));
  assert.ok(!paths.includes('/api/tokenhealth'));
});


test('public alerts page renders signup wired to backend alert APIs', () => {
  assert.deepEqual(parsePublicRoute('/alerts'), { page: 'alerts' });
  const html = renderPublicPortalHtml(mockDashboardData, { page: 'alerts' });
  assert.match(html, /Sky alerts/);
  assert.match(html, /Alert preferences/);
  assert.match(html, /Email/);
  assert.match(html, /Full moon \/ supermoon/);
  assert.match(html, /Meteor showers/);
  assert.match(html, /Eclipses/);
  assert.match(html, /Special event videos/);
  assert.match(html, /Daily sky guide reminders/);
  assert.match(html, /Submit to create or update an email sky-alert subscription/);
  assert.match(html, /Save alert subscription/);
  assert.match(html, /Add to calendar/);
  assert.match(html, /Share link/);
  assert.doesNotMatch(html, /accessToken|clientSecret|connectionString/i);
});
