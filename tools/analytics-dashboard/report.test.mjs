import assert from 'node:assert/strict';
import test from 'node:test';
import { createUmamiClient } from './umami.mjs';
import { summarize } from './report.mjs';

function event(name, sessionId, createdAt, properties) {
  return { name, sessionId, createdAt, id: String(createdAt), properties };
}

test('counts installations once, picks the latest version and tracks feature reach', () => {
  const installs = new Map([['s1', 'a'], ['s2', 'a'], ['s3', 'b']]);
  const report = summarize([
    event('heartbeat', 's2', 4, { app_version: '1.1.0' }),
    event('app_started', 's1', 1, { app_version: '1.0.0' }),
    event('feature_used', 's1', 2, { app_version: '1.0.0', feature: 'tab_open' }),
    event('feature_used', 's2', 3, { app_version: '1.1.0', feature: 'tab_open' }),
    event('feature_used', 's3', 5, { app_version: '1.0.0', feature: 'restore_tabs' }),
    event('feature_used', 'unidentified', 6, { app_version: '1.0.0', feature: 'tab_open' }),
  ], installs);

  assert.equal(report.activeInstallations, 2);
  assert.equal(report.launches, 1);
  assert.equal(report.featureEvents, 4);
  assert.equal(report.unknownSessions, 1);
  assert.deepEqual(report.versions, [
    { version: '1.0.0', installations: 1, share: 0.5 },
    { version: '1.1.0', installations: 1, share: 0.5 },
  ]);
  assert.deepEqual(report.features, [
    { feature: 'tab_open', uses: 3, installations: 1, reach: 0.5 },
    { feature: 'restore_tabs', uses: 1, installations: 1, reach: 0.5 },
  ]);
});

test('calculates percentiles from numeric timings and counts historical buckets separately', () => {
  const report = summarize([
    event('app_started', 'a', 1, { duration_ms: '800.0000', startup_ms: '<1s' }),
    event('app_started', 'a', 2, { startup_ms: '<3s' }),
    event('app_started', 'a', 3, { duration_ms: 0, startup_ms: '<100ms' }),
    event('app_started', 'a', 4, { duration_ms: '', startup_ms: '<3s' }),
    event('app_started', 'a', 5, { duration_ms: 1200 }),
    event('cluster_connected', 'a', 6, { result: 'ok' }),
  ], new Map([['a', 'install-a']]));

  assert.deepEqual(report.latency, [
    { operation: 'App startup', samples: 3, p50: 800, p95: 1200, buckets: [{ range: '<3s', count: 2 }], untimed: 0 },
    { operation: 'Fetch messages', samples: 0, p50: null, p95: null, buckets: [], untimed: 0 },
    { operation: 'Load topics', samples: 0, p50: null, p95: null, buckets: [], untimed: 1 },
  ]);
});

test('paginates the Umami pivot and joins events with their installations', async () => {
  const websiteId = '00000000-0000-4000-8000-000000000001';
  const origins = new Set();
  const pages = [];
  function mockFetch(url, options) {
    origins.add(url.origin);
    assert.equal(options.redirect, 'error');
    assert.equal(options.headers.Authorization, 'Bearer fake-token');

    if (url.pathname.endsWith('/sessions/session1')) {
      return Response.json({ distinctId: 'install1', distinctIds: ['install1'] });
    }
    assert.equal(url.pathname, `/api/websites/${websiteId}/event-data-pivot`);
    assert.equal(url.searchParams.get('startAt'), '1000');
    assert.equal(url.searchParams.get('endAt'), '2000');
    if (url.searchParams.get('eventName') !== 'app_started') {
      return Response.json({ data: [], count: 0, pageSize: 100 });
    }
    const page = url.searchParams.get('page');
    pages.push(page);
    return Response.json({
      data: [{
        eventId: `event-${page}`,
        sessionId: 'session1',
        createdAt: new Date(1000 + Number(page)).toISOString(),
        propertyKeys: ['app_version', 'duration_ms'],
        propertyValues: [`1.0.${page}`, `${page}00.0000`],
      }],
      count: 2,
      pageSize: 1,
    });
  }
  const loadReport = createUmamiClient({
    origin: 'https://analytics.example.test', websiteId, token: 'fake-token', fetchImpl: mockFetch,
  });
  const report = await loadReport(1000, 2000);

  assert.deepEqual([...origins], ['https://analytics.example.test']);
  assert.deepEqual(pages, ['1', '2']);
  assert.equal(report.activeInstallations, 1);
  assert.equal(report.launches, 2);
  assert.equal(report.versions[0].version, '1.0.2');
  assert.equal(report.latency[0].p95, 200);
  assert.doesNotMatch(JSON.stringify(report), /install1|session1|fake-token/);
});
