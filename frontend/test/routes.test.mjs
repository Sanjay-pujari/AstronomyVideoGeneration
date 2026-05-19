import assert from 'node:assert/strict';
import test from 'node:test';
import { resolveDashboardPage } from '../dist/assets/ui/routes.js';

test('hash analytics resolves analytics page', () => {
  assert.equal(resolveDashboardPage('/admin', '#analytics').page, 'analytics');
});

test('hash ai-optimization resolves ai optimization page', () => {
  assert.equal(resolveDashboardPage('/admin', '#ai-optimization').page, 'ai-optimization');
});

test('/admin/analytics path resolves analytics page', () => {
  assert.equal(resolveDashboardPage('/admin/analytics', '').page, 'analytics');
});

test('/admin/ai-optimization path resolves ai optimization page', () => {
  assert.equal(resolveDashboardPage('/admin/ai-optimization', '').page, 'ai-optimization');
});

test('unknown hash falls back to dashboard with warning', () => {
  const resolved = resolveDashboardPage('/admin', '#not-a-page');
  assert.equal(resolved.page, 'dashboard');
  assert.equal(resolved.unknownPage, 'not-a-page');
});
