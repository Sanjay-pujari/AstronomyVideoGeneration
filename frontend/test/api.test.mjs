import assert from 'node:assert/strict';
import test from 'node:test';
import { ApiError, api, removeSecrets } from '../dist/assets/services/api.js';

test('api client sanitizes secret-shaped fields', () => {
  assert.deepEqual(removeSecrets({ ok: true, accessToken: 'hidden', nested: { appSecret: 'hidden', status: 'safe' } }), { ok: true, nested: { status: 'safe' } });
});

test('api client wraps failed responses', async () => {
  globalThis.fetch = async () => new Response(JSON.stringify({ message: 'bad', refreshToken: 'hidden' }), { status: 500 });
  await assert.rejects(api.getRegions(), (error) => error instanceof ApiError && error.status === 500);
});

test('api client wraps network failures', async () => {
  globalThis.fetch = async () => { throw new Error('offline'); };
  await assert.rejects(api.getRegions(), ApiError);
});


test('frontend alert form client calls backend subscribe endpoint', async () => {
  let captured;
  globalThis.fetch = async (url, init) => {
    captured = { path: new URL(url).pathname, init };
    return new Response(JSON.stringify({ subscriberId: 'sub-1', email: 'viewer@example.com', regionId: 'india-udaipur', language: 'en', isActive: true, preferences: { eventTypes: ['MeteorShower'], preferredAlertTimeLocal: '18:00', minimumEventScore: 0.65 } }), { status: 200 });
  };

  await api.subscribeToAlerts({ email: 'viewer@example.com', regionId: 'india-udaipur', language: 'en', eventTypes: ['MeteorShower'], preferredAlertTimeLocal: '18:00', minimumEventScore: 0.65 });

  assert.equal(captured.path, '/api/alerts/subscribe');
  assert.equal(captured.init.method, 'POST');
  assert.match(String(captured.init.body), /MeteorShower/);
});

test('api exposes newly wired admin endpoint methods', () => {
  const required = [
    'getOpsRuns','getOpsRun','getOpsFailures','getOpsSummary','getOpsPipelinesRecent','getOpsPipelineStages','getOpsFailuresRecent','getOpsJobsSummary',
    'getPipelinesRecent','getPipelineById','getThumbnailPublishStatus','resumePipeline','retryPublish','retryYoutubePublish','retryMetaPublish',
    'getSchedulerEventPlan','enableSchedule','disableSchedule','enableRegion','disableRegion',
    'getEventById','refreshEvents','generateEvent','getGeneratedEvents',
    'getAnalyticsInsights','getAnalyticsPlatformSummary','getAnalyticsContentPerformance','getAnalyticsRecent','getAnalyticsPlatform','getAnalyticsRun','collectAnalyticsNow','getAnalyticsTopPerforming','getAnalyticsYoutubeVideo',
    'getAiOptimizationRecommendations','generateAiOptimizationNow','getAiOptimizationPendingApproval','applyAiOptimizationApproved','rejectAiOptimization','getAiOptimizationTrends',
    'getOptimizationPlan','applyOptimizationPreview','getYoutubeTokenHealth','getMetaTokenHealth',
    'getCelestialAssetStatus','refreshCelestialAssetStatus','getCelestialAsset'
  ];
  for (const key of required) assert.equal(typeof api[key], 'function', `missing ${key}`);
});
