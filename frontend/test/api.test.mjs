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
