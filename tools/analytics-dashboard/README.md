# KafkaLens desktop analytics

Run the dashboard locally with Node.js 22 or newer. Use an Umami account with access to
the KafkaLens App property, including Events and Sessions. Supply a bearer token in
`UMAMI_TOKEN`, or let the dashboard sign in with the account credentials:

```bash
export UMAMI_USERNAME=your-umami-username
read -rs -p 'Umami password: ' UMAMI_PASSWORD
echo
export UMAMI_PASSWORD
node --use-env-proxy tools/analytics-dashboard/server.mjs
unset UMAMI_PASSWORD
```

Open [http://127.0.0.1:4317](http://127.0.0.1:4317). Current Node 22 releases support
`--use-env-proxy`. On earlier Node 22 releases, omit that option if no proxy is required.
The server binds to loopback and makes read-only requests to Umami after sign-in.
Credentials and raw IDs stay on the server. The browser receives aggregate counts and
timings. Run the dashboard on a trusted machine. To use another Umami instance or
property, set `UMAMI_URL` to its HTTPS origin and `UMAMI_WEBSITE_ID` to the property UUID.

## How counts are calculated

- **Active installations** are distinct anonymous install IDs that sent a first run,
  launch, heartbeat, feature use, fetch completion, or topic-load event during the range.
  Multiple Umami sessions with the same install ID count once. Sessions Umami cannot
  attribute to a single install ID contribute to event totals only.
- **Version adoption** assigns each active installation to the version on its most recent
  included event. **Feature reach** divides known installations using a feature by the
  number of known active installations.
- **Operation latency** uses nearest-rank p50 and p95 over the numeric samples in the
  selected window, including completed failure outcomes when timed. Older startup and
  fetch events appear in their original buckets. A dash means no percentile is
  available. Topic timing measures the tab's wait for loading, including retries when
  applicable. A request satisfied by the in-memory topic list contributes no timing.
- The existing Umami Overview counts pageviews. The desktop app uses named events, and
  its event collection remains independent of website traffic.

Run the report and integration tests with:

```bash
node --test tools/analytics-dashboard/*.test.mjs
```
