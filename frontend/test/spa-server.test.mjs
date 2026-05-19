import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, mkdir, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { resolveRequest } from '../scripts/spa-server.mjs';

async function setupDistFixture() {
  const root = await mkdtemp(path.join(os.tmpdir(), 'spa-server-'));
  await mkdir(path.join(root, 'assets/styles'), { recursive: true });
  await writeFile(path.join(root, 'index.html'), '<!doctype html><html><body>SPA</body></html>');
  await writeFile(path.join(root, 'assets/main.js'), 'console.log("main")');
  await writeFile(path.join(root, 'assets/styles/app.css'), 'body{}');
  await writeFile(path.join(root, 'frontend-api-health.json'), '{"ok":true}');
  return root;
}

test('known assets are served directly', async () => {
  const root = await setupDistFixture();
  const jsResponse = await resolveRequest(root, '/assets/main.js');
  assert.equal(jsResponse.status, 200);
  assert.equal(jsResponse.contentType, 'text/javascript; charset=utf-8');
  assert.match(jsResponse.body.toString('utf8'), /main/);

  const cssResponse = await resolveRequest(root, '/assets/styles/app.css');
  assert.equal(cssResponse.status, 200);
  assert.equal(cssResponse.contentType, 'text/css; charset=utf-8');

  const healthResponse = await resolveRequest(root, '/frontend-api-health.json');
  assert.equal(healthResponse.status, 200);
  assert.equal(healthResponse.contentType, 'application/json; charset=utf-8');
});

test('unknown SPA paths fallback to index.html', async () => {
  const root = await setupDistFixture();

  for (const route of ['/admin', '/dashboard', '/admin/analytics', '/dashboard/analytics']) {
    const response = await resolveRequest(root, route);
    assert.equal(response.status, 200);
    assert.equal(response.contentType, 'text/html; charset=utf-8');
    assert.match(response.body.toString('utf8'), /SPA/);
  }
});
