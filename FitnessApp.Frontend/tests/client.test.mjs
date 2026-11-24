import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';

const source = await readFile(new URL('../src/api/client.ts', import.meta.url), 'utf8');
let moduleId = 0;
async function client(t, fetch) {
  t.mock.method(globalThis, 'fetch', fetch);
  const code = ts.transpileModule(source.replaceAll('import.meta.env', '{}'), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext }
  }).outputText;
  return import(`data:text/javascript;base64,${Buffer.from(code).toString('base64')}#${moduleId++}`);
}
const json = (data, status = 200, headers = {}) => Response.json(data, { status, headers });

test('profile response supplies CSRF header for subsequent JSON mutations', async t => {
  const api = await client(t, async (url, options) => {
    assert.equal(options.credentials, 'include');
    if (url === '/api/users/me') return json({ id: 1 }, 200, { RequestVerificationToken: 'signed-token' });
    assert.equal(url, '/api/visits');
    assert.equal(options.headers.RequestVerificationToken, 'signed-token');
    assert.equal(options.headers['Content-Type'], 'application/json');
    assert.deepEqual(JSON.parse(options.body), { customerPassId: 1 });
    return new Response(null, { status: 204 });
  });
  await api.request('/api/users/me');
  assert.equal(await api.request('/api/visits', 'POST', { customerPassId: 1 }), undefined);
});

test('late unauthorized responses share a completed refresh instead of rotating again', async t => {
  let releaseLate;
  const late = new Promise(resolve => { releaseLate = resolve; });
  let refreshes = 0;
  const calls = new Map();
  const api = await client(t, async url => {
    if (url === '/api/session/antiforgery') return json({ token: 'csrf' });
    if (url === '/api/session/refresh') { refreshes++; return json({ id: 1 }); }
    const count = (calls.get(url) ?? 0) + 1; calls.set(url, count);
    if (count === 1) {
      if (url === '/api/late') await late;
      return json({}, 401);
    }
    return json({ ok: true });
  });
  const first = api.request('/api/first');
  const second = api.request('/api/late');
  await first;
  releaseLate();
  assert.deepEqual(await second, { ok: true });
  assert.equal(refreshes, 1);
});

test('binary download refreshes expired authentication and preserves bytes', async t => {
  let downloads = 0;
  const api = await client(t, async url => {
    if (url === '/api/session/antiforgery') return json({ token: 'csrf' });
    if (url === '/api/session/refresh') return json({ id: 1 });
    if (++downloads === 1) return json({}, 401);
    return new Response(new Uint8Array([80, 75, 3, 4]));
  });
  assert.deepEqual(new Uint8Array(await (await api.download('/api/statistics/ExportExcel')).arrayBuffer()), new Uint8Array([80, 75, 3, 4]));
  assert.equal(downloads, 2);
});

test('failed refresh propagates unauthorized without infinite retries', async t => {
  let refreshes = 0;
  const api = await client(t, async url => {
    if (url === '/api/session/antiforgery') return json({ token: 'csrf' });
    if (url === '/api/session/refresh') refreshes++;
    return json({ error: 'Session expired' }, 401);
  });
  await assert.rejects(api.request('/api/users/me'), error => error.status === 401 && error.message === 'Session expired');
  assert.equal(refreshes, 1);
});
