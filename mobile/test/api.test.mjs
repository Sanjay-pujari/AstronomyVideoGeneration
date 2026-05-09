import assert from 'node:assert/strict';
import test from 'node:test';
import { ApiError, api, sanitizePayload } from '../.test-build/services/api.js';

test('mobile api sanitizes secret-shaped fields', () => {
  assert.deepEqual(sanitizePayload({ ok: true, sasToken: 'hidden', nested: { connectionString: 'hidden', label: 'safe' } }), { ok: true, nested: { label: 'safe' } });
});

test('mobile api wraps failed responses', async () => {
  globalThis.fetch = async () => new Response(JSON.stringify({ message: 'Nope', refreshToken: 'hidden' }), { status: 401 });
  await assert.rejects(api.getRegions(), (error) => error instanceof ApiError && error.status === 401);
});
