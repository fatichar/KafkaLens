import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { createUmamiClient } from './umami.mjs';

const files = new Map([
  ['/', ['index.html', 'text/html']],
  ['/dashboard.css', ['dashboard.css', 'text/css']],
  ['/dashboard.js', ['dashboard.js', 'text/javascript']],
  ['/favicon.png', ['../../docs/assets/images/KafkaLens_logo_cyan_512.png', 'image/png']],
].map(([route, [file, type]]) => [route, [readFileSync(new URL(file, import.meta.url)), type]]));

const host = '127.0.0.1';
const day = 86_400_000;
const ranges = new Set([7, 30, 90]);
const securityHeaders = {
  'Cache-Control': 'no-store',
  'Content-Security-Policy': "default-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'",
  'X-Content-Type-Options': 'nosniff',
};

function reply(response, status, type, body) {
  const contentType = type.startsWith('text/') || type === 'application/json' ? `${type}; charset=utf-8` : type;
  response.writeHead(status, { ...securityHeaders, 'Content-Type': contentType });
  response.end(body);
}

export function startDashboard(loadReport, port = 4317) {
  let pending;
  const server = createServer(async (request, response) => {
    const localOrigin = `http://${host}:${server.address().port}`;
    if (request.headers.host !== new URL(localOrigin).host ||
        (request.headers.origin && request.headers.origin !== localOrigin) ||
        request.headers['sec-fetch-site'] === 'cross-site') {
      return reply(response, 403, 'text/plain', 'Open this dashboard directly on its local address.');
    }
    if (request.method !== 'GET') return reply(response, 405, 'text/plain', 'Use GET.');
    const url = URL.parse(request.url, localOrigin);
    if (!url) return reply(response, 400, 'text/plain', 'Invalid request URL.');
    if (files.has(url.pathname)) {
      const [body, type] = files.get(url.pathname);
      return reply(response, 200, type, body);
    }
    if (url.pathname !== '/api/report') return reply(response, 404, 'text/plain', 'Not found.');
    const days = Number(url.searchParams.get('days') ?? 30);
    if (!ranges.has(days)) return reply(response, 400, 'text/plain', 'Select a supported date range.');

    if (pending && pending.days !== days) {
      return reply(response, 429, 'text/plain', 'Another report is loading. Try refreshing in a moment.');
    }
    const endAt = Date.now();
    pending ??= {
      days,
      result: Promise.resolve().then(() => loadReport(endAt - days * day, endAt))
        .finally(() => { pending = undefined; }),
    };
    try {
      return reply(response, 200, 'application/json', JSON.stringify(await pending.result));
    } catch (error) {
      console.error(error.message);
      const message = error.message.startsWith('Umami capped')
        ? error.message
        : 'Unable to load Umami data. Check the server connection and credentials and try again.';
      return reply(response, 502, 'text/plain', message);
    }
  });
  return server.listen(port, host);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const loadReport = createUmamiClient({
    origin: process.env.UMAMI_URL || 'https://analytics.greenfit.in',
    websiteId: process.env.UMAMI_WEBSITE_ID || 'cf83f60c-42eb-4829-bccf-e8281d54e66f',
    token: process.env.UMAMI_TOKEN,
    username: process.env.UMAMI_USERNAME,
    password: process.env.UMAMI_PASSWORD,
  });
  const server = startDashboard(loadReport);
  server.on('listening', () => console.log(`KafkaLens analytics at http://${host}:${server.address().port}`));
}
