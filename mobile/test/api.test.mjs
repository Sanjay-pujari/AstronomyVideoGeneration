import assert from 'node:assert/strict';
import test from 'node:test';
import { ApiError, api, createSafeExternalLink, sanitizePayload } from '../.test-build/services/api.js';
import { isMockModeEnabled } from '../.test-build/config/environment.js';

test('mobile api sanitizes secret-shaped fields', () => {
  assert.deepEqual(sanitizePayload({ ok: true, sasToken: 'hidden', nested: { connectionString: 'hidden', label: 'safe' } }), { ok: true, nested: { label: 'safe' } });
});

test('mobile api wraps failed responses with a friendly ApiError', async () => {
  globalThis.fetch = async () => new Response(JSON.stringify({ message: 'Nope', refreshToken: 'hidden' }), { status: 401 });
  await assert.rejects(api.getRegions(), (error) => error instanceof ApiError && error.status === 401 && error.message === 'Nope');
});

test('external links are sanitized and unsafe schemes are rejected', () => {
  assert.equal(createSafeExternalLink('javascript:alert(1)'), undefined);
  assert.equal(createSafeExternalLink('https://example.com/video?sig=hidden&v=123#token'), 'https://example.com/video?v=123');
});

test('mock mode is disabled by default for production safety', () => {
  assert.equal(isMockModeEnabled(), false);
});
