import assert from 'node:assert/strict';
import test from 'node:test';
import { isAdminRoute, resolveDashboardPage } from '../dist/assets/ui/routes.js';

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

test('/dashboard/analytics path resolves analytics page', () => {
  assert.equal(resolveDashboardPage('/dashboard/analytics', '').page, 'analytics');
});

test('unknown hash falls back to dashboard with warning', () => {
  const resolved = resolveDashboardPage('/admin', '#not-a-page');
  assert.equal(resolved.page, 'dashboard');
  assert.equal(resolved.unknownPage, 'not-a-page');
});

test('public portal paths are not treated as admin routes', () => {
  for (const route of ['/', '/events', '/alerts', '/tonights-sky', '/videos', '/about', '/regions/india']) {
    assert.equal(isAdminRoute(route), false, route);
  }
});

test('admin entry paths are treated as admin routes', () => {
  for (const route of ['/admin', '/admin/analytics', '/dashboard', '/dashboard/pipeline-runs', '/pipeline-runs', '/analytics']) {
    assert.equal(isAdminRoute(route), true, route);
  }
});
