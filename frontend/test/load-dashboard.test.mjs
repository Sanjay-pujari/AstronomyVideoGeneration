import assert from 'node:assert/strict';
import test from 'node:test';
import { getFrontendApiHealth, loadDashboardData } from '../dist/assets/services/api.js';

const json = (body) => new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });

test('loadDashboardData uses analytics dashboard and top-content APIs without TikTok', async () => {
  globalThis.fetch = async (url) => {
    const path = new URL(url).pathname;
    if (path === '/api/analytics/dashboard') return json({
      overallSummary: { totalViews: 3210, totalEngagement: 222, averageEngagementRate: 6.9, bestPerformingPlatform: 'Instagram', totalContentPublished: 2 },
      platformBreakdown: [
        { platform: 'YouTube', contentCount: 1, totalViews: 1200, averageEngagement: 5.1 },
        { platform: 'TikTok', contentCount: 9, totalViews: 999999, averageEngagement: 9.9 },
        { platform: 'Instagram', contentCount: 1, totalViews: 2010, averageEngagement: 7.2 }
      ],
      regionBreakdown: [],
      charts: {}
    });
    if (path === '/api/analytics/top-content') return json([
      { id: 'yt-1', title: 'Moon guide', platform: 'YouTube', contentType: 'Video', publishedUtc: '2026-05-01T00:00:00Z', views: 1200, engagement: 50 },
      { id: 'tt-1', title: 'Should not show', platform: 'TikTok', contentType: 'Short', views: 999999 },
      { id: 'ig-1', title: 'Mars reel', platform: 'Instagram', contentType: 'Reel', publishedUtc: '2026-05-02T00:00:00Z', views: 2010, engagement: 172 }
    ]);
    if (path === '/api/ops/dashboard') return json({ publishStatuses: [{ platform: 'TikTok', status: 'healthy' }, { platform: 'Facebook', status: 'healthy' }] });
    if (path === '/api/scheduler/status') return json({});
    if (path === '/api/regions') return json([]);
    if (path === '/api/events/upcoming') return json([]);
    if (path === '/api/events/top') return json([]);
    if (path === '/api/tokenhealth') return json([]);
    return json({});
  };

  const data = await loadDashboardData();
  assert.equal(data.analytics.totalViews, 3210);
  assert.equal(data.analytics.totalEngagement, 222);
  assert.equal(data.analytics.bestPlatform, 'Instagram');
  assert.deepEqual(data.analyticsDashboard.platformBreakdown?.map((item) => item.platform), ['YouTube', 'Instagram']);
  assert.deepEqual(data.analytics.topContent?.map((item) => item.platform), ['YouTube', 'Instagram']);
  assert.equal(data.latestVideos.length, 1);
  assert.equal(data.latestShorts.length, 1);
  assert.deepEqual(data.publishStatuses.map((item) => item.platform), ['Facebook']);
});

test('frontend API health records endpoint success and failure diagnostics', async () => {
  globalThis.fetch = async (url) => {
    const path = new URL(url).pathname;
    if (path === '/api/analytics/dashboard') return new Response(JSON.stringify({ message: 'down' }), { status: 503 });
    return json([]);
  };

  await loadDashboardData();
  const report = getFrontendApiHealth();
  assert.ok(report.endpoints.some((entry) => entry.endpoint === '/api/analytics/dashboard' && entry.success === false && entry.fallbackUsed === true));
  assert.ok(report.endpoints.every((entry) => typeof entry.responseTimeMs === 'number'));
});
