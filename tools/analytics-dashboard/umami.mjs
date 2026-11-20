import { eventNames, summarize } from './report.mjs';

const PAGE_SIZE = 100;

export function createUmamiClient({ origin, websiteId, token, username, password, fetchImpl = fetch }) {
  const base = new URL(origin);
  if (base.protocol !== 'https:' || base.origin !== base.href.slice(0, -1)) {
    throw new Error('UMAMI_URL must be an HTTPS origin.');
  }
  if (!/^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(websiteId)) {
    throw new Error('UMAMI_WEBSITE_ID must be a UUID.');
  }
  if (!token && !(username && password)) {
    throw new Error('Set UMAMI_TOKEN or both UMAMI_USERNAME and UMAMI_PASSWORD.');
  }

  let loginPromise;
  async function request(path, options) {
    const response = await fetchImpl(new URL(path, base), {
      ...options,
      redirect: 'error',
      signal: AbortSignal.timeout(15_000),
    });
    if (!response.ok) {
      throw Object.assign(new Error(`Umami returned HTTP ${response.status}.`), { status: response.status });
    }
    return response.json();
  }

  async function login() {
    const response = await request('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password }),
    });
    if (!response.token) throw new Error('Umami did not return an access token.');
    return response.token;
  }

  function accessToken() {
    if (token) return token;
    loginPromise ??= login().catch(error => {
      loginPromise = undefined;
      throw error;
    });
    return loginPromise;
  }

  async function get(path, params = {}) {
    const query = new URLSearchParams(params);
    const url = `/api/websites/${websiteId}/${path}?${query}`;
    const credentials = accessToken();
    const send = async value => request(url, { headers: { Authorization: `Bearer ${await value}` } });
    try {
      return await send(credentials);
    } catch (error) {
      if (token || ![401, 403].includes(error.status)) throw error;
      if (loginPromise === credentials) loginPromise = undefined;
      return send(accessToken());
    }
  }

  async function readEvents(eventName, period) {
    const events = new Map();
    for (let page = 1; ; page++) {
      const result = await get('event-data-pivot', {
        ...period, eventName, page, pageSize: PAGE_SIZE,
      });
      if (result.isCapped) {
        throw new Error('Umami capped the event query. Select a shorter date range.');
      }
      for (const entry of result.data) {
        events.set(entry.eventId, {
          id: entry.eventId,
          sessionId: entry.sessionId,
          name: eventName,
          createdAt: Date.parse(entry.createdAt),
          properties: Object.fromEntries(entry.propertyKeys.map((key, index) => [key, entry.propertyValues[index]])),
        });
      }
      if (page * result.pageSize >= result.count || !result.data.length) break;
    }
    return [...events.values()];
  }

  async function readInstallations(events) {
    const installations = new Map();
    const sessions = [...new Set(events.map(event => event.sessionId))];
    const batchSize = 6;
    for (let offset = 0; offset < sessions.length; offset += batchSize) {
      await Promise.all(sessions.slice(offset, offset + batchSize).map(async sessionId => {
        try {
          const session = await get(`sessions/${encodeURIComponent(sessionId)}`);
          const ids = session.distinctIds ?? (session.distinctId ? [session.distinctId] : []);
          if (ids.length === 1 && ids[0]) installations.set(sessionId, ids[0]);
        } catch (error) {
          // A session may have been deleted between the event and session queries.
          if (error.status !== 404) throw error;
        }
      }));
    }
    return installations;
  }

  return async function loadReport(startAt, endAt) {
    const groups = await Promise.all(eventNames.map(name => readEvents(name, { startAt, endAt })));
    const events = groups.flat();
    const installations = await readInstallations(events);
    return { startAt, endAt, ...summarize(events, installations) };
  };
}
