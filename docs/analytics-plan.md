# Product Analytics Plan (pre-1.0)

Status: **Approved and implemented.** Decisions: opt-out with first-run notice, self-hosted Umami,
13-month raw retention, full event list for 1.0, plus unexpected-failure reporting (`app_error`).
The authoritative event list is `ViewModels/Telemetry/TelemetrySchema.cs` / `docs/privacy.html`.

## 1. Problem

Before 1.0 we need usage data to decide:

- which features matter (invest), which are unused (simplify / hide / remove),
- what settings people change and to what (→ better shipped defaults),
- platform/version mix (what to test, when to drop old versions),
- engagement (DAU / WAU / MAU, retention, time spent, fetch volume).

Today the website (GitHub Pages) sends page views + download events to a self-hosted
Umami (`analytics.greenfit.in`). The desktop app sends nothing.

## 2. Goals / Non-goals

Goals
- Anonymous, aggregate product analytics from the desktop app.
- Join website funnel (visit → download) with app funnel (first launch → active) where possible.
- Zero impact on UI responsiveness and fetch performance; works offline.

Non-goals
- No full crash dumps or exception messages. Failures are reported as type + method-name frames only (`app_error`).
- No per-user profiling, no session replay, no A/B framework.
- No change to the website hosting (see §8).

## 3. Privacy principles (hard rules)

KafkaLens users are engineers pointing it at production clusters. Trust is the product.

**Never collected:** cluster names, broker/bootstrap hosts, topic names, partition ids, consumer
groups, message keys/values/headers, filter text, file paths, usernames, machine names,
exception messages, plugin config.

**Collected:** random install id, app version, OS / arch / runtime, enumerated feature
events, enumerated/bucketed setting values, bucketed counts and durations.

**Controls**
- First-run notice dialog with toggle (see §9 for opt-in vs opt-out decision).
- `Preferences → Privacy → Send anonymous usage data` toggle, takes effect immediately.
- Env overrides: `KAFKALENS_TELEMETRY=0` and standard `DO_NOT_TRACK=1` force-disable.
- `Help → View telemetry data` shows the last N events actually sent (transparency).
- Public `kafkalens.com/privacy.html` listing every event + property (generated from code).
- Install id is resettable (new GUID) from Preferences.
- Debug builds and `IntegrationTests` never send.

## 4. Metrics → decisions

| Question | Metric | Source event(s) |
|---|---|---|
| How many active users? | DAU / WAU / MAU, stickiness (DAU/MAU) | `app_started`, `heartbeat` (distinct `install_id`) |
| Do people come back? | D1 / D7 / D30 retention by first-seen cohort | `app_first_run`, any event |
| How long is it used? | Active minutes / day / install, session length | `heartbeat`, `app_exited` |
| Which versions / OS? | Share by `app_version`, `os`, `arch`, `install_kind` | common props |
| Update adoption | Time from release to 50% on new version | `app_version` over time, `update_*` |
| How much do they fetch? | Fetches/day, messages fetched (bucketed), fetch duration | `fetch_completed` |
| Which fetch modes? | Position (start/end/offset/time), direction, count | `fetch_completed` |
| Which features are used? | % of WAU using feature X; events/user | `feature_used` |
| Unused features? | Features with < N% WAU → simplify/hide | `feature_used` |
| Which defaults to ship? | Distribution of each setting value; % changed from default | `settings_snapshot` |
| Which formatters matter? | Formatter selected / auto-detected share | `formatter_selected` |
| Connection types | Local vs gRPC vs REST client; # clusters (bucket) | `settings_snapshot`, `cluster_connected` |
| Plugin ecosystem | Plugins installed (public repo ids only), themes | `settings_snapshot`, `plugin_installed` |
| Friction | Fetch error rate by error *category*, cancel rate | `fetch_completed.result` |
| Website → app conversion | downloads vs first runs per day / per `utm_source` | website `download` + `app_first_run` |

## 5. Event catalog (v1)

### Common properties (on every event)

| Prop | Example | Notes |
|---|---|---|
| `install_id` | random GUID | generated on first run, stored in data dir |
| `session_id` | random GUID | per process launch |
| `app_version` | `1.0.0` | from assembly |
| `os` | `windows` / `macos` / `linux` | |
| `os_version` | `10.0.22631` | major.minor.build only |
| `arch` | `x64` / `arm64` | |
| `install_kind` | `installer` / `zip` / `msix` | detect from install path / package context |
| `locale` | `en-US` | UI culture |
| `screen` | `2560x1440` | primary screen, optional |
| `schema` | `1` | event schema version |

### Lifecycle

| Event | Props |
|---|---|
| `app_first_run` | `utm_source` (if passed via installer / download URL, optional) |
| `app_started` | `startup_ms` (bucket), `restored_tabs` (bucket), `plugins_loaded` (count) |
| `heartbeat` | every 5 min **while window focused and user input within last 5 min**; `active_s` since last heartbeat |
| `app_exited` | `session_active_s`, `session_wall_s` |

Time spent = sum of `active_s` (heartbeats are also the DAU source if `app_started` is lost).

### Usage

| Event | Props |
|---|---|
| `cluster_connected` | `client_kind` (local/grpc/rest), `result` (ok/error-category), `topics_bucket` |
| `fetch_completed` | `client_kind`, `node` (topic/partition), `position` (start/end/offset/time), `direction`, `requested` (exact value from the fixed list, else bucket), `received_bucket`, `duration_ms_bucket`, `result` (ok/cancelled/timeout/auth/network/other) |
| `feature_used` | `feature` (enum below), `source` (menu/shortcut/context/button) |
| `formatter_selected` | `formatter` (built-in name, or `plugin` for plugin formatters), `target` (key/value), `auto` (bool) |
| `update_check` / `update_installed` | `result`, `from_version`, `to_version` |
| `plugin_installed` / `plugin_removed` | `plugin_id` only if from the official repo, else `third_party` |

`feature` enum (initial, derived from current commands):
`save_messages`, `open_saved_messages`, `copy_value`, `copy_key`, `copy_offset`,
`copy_timestamp`, `copy_all`, `positive_filter`, `negative_filter`, `topic_filter`,
`sort_messages`, `message_details_open`, `navigator_toggle`, `fetch_panel_toggle`,
`tab_new`, `tab_close`, `tab_switch_shortcut`, `edit_clusters`, `refresh_clusters`,
`preferences_open`, `plugin_manager_open`, `theme_changed`, `app_log_open`,
`apply_formatter_all_clusters`, `topic_settings_saved`.

Filter events fire **once per fetch result view** (debounced), never per keystroke, and carry no text.

### `settings_snapshot` (once per day per install, and on Preferences close if changed)

`theme`, `font_size`, `default_fetch_count`, `fetch_counts_customized` (bool),
`restore_tabs_on_startup`, `eager_load_topics`, `show_save_notification`,
`saved_dir_customized` (bool), `auto_check_updates`, `connection_check_interval_s`,
`hidden_key_formatters_count`, `hidden_value_formatters_count`,
`kafka_*` timeouts / `fetch_max_bytes` as `default` or bucket,
`clusters_bucket`, `clients_by_kind` (local/grpc/rest counts), `plugins` (official ids),
`telemetry_notice_version`.

For every setting we also report `<name>_is_default` so "% users changing the default" is trivial.

### Bucketing

Counts: `0, 1, 2-5, 6-10, 11-50, 51-100, 101-500, 501-1k, 1k-5k, 5k-10k, 10k-50k, 50k+`.
Durations: `<100ms, <500ms, <1s, <3s, <10s, <30s, 30s+`. Exact `requested` fetch count is fine
because it comes from a fixed preset list (useful for choosing presets/default).

## 6. Client architecture (app)

```
ViewModels ──► ITelemetry (Shared)  ── Track(name, props) — non-blocking, never throws
                     │
                     ▼
            TelemetryService (ViewModels/Services)
               • enabled check (setting + env + DEBUG)
               • adds common props, validates against allow-listed schema
               • Channel<TelemetryEvent> (bounded, DropOldest)
                     │  background loop, honours CancellationToken
                     ▼
            ITelemetrySink  ── UmamiSink (v1) │ HttpJsonSink (future) │ NullSink
               • batch flush every 60s / 50 events / on exit (2s cap)
               • offline spool: <data>/telemetry/queue.jsonl, max 1 MB / 7 days, drop oldest
               • exp. backoff, honour 429/Retry-After, server kill-switch (§7)
```

- `ITelemetry` in `Shared` so Core-free modules and plugins (optionally, later) can use it.
- Event names/props are **constants + typed helpers** (`Telemetry.FetchCompleted(...)`), not free strings,
  so `privacy.html` and a unit test can enumerate the full schema.
- `ActivityTracker` (KafkaLens.UI layer, thin): window focus + last-input timestamp → exposes
  `IsActive` to `TelemetryService` via an interface. No logic in Views beyond wiring.
- Wiring points: `App.axaml.cs` (DI, start/exit), `MainViewModel.*` (menus, tabs, updates,
  preferences), `OpenedClusterViewModel.Fetching.cs` (`OnStreamFinished` → `fetch_completed`),
  `MessagesViewModel` (copy / filter / sort), `PluginManagerViewModel`, `PreferencesViewModel`.
- Fetch path: one `Track` call when the stream finishes. Zero per-message cost.
- No new NuGet dependency for v1 (`HttpClient` + `System.Text.Json` + `System.Threading.Channels`).
- Tests (`ViewModels.Tests`): fake sink; assert events fired, disabled state sends nothing,
  props never contain values outside allow-list, buckets correct, spool caps respected.

## 7. Backend options

The app must talk to **an endpoint we own** (`https://t.kafkalens.com/…`), never directly to a
vendor/host URL. That lets us swap the backend without shipping an app update.

### Option A — Reuse self-hosted Umami (recommended for 1.0)

- VPS reverse proxy: `t.kafkalens.com/api/send` → existing Umami. New Umami "website" for the app.
- App posts Umami's event payload (`type: "event"`, `website`, `url: "app://kafkalens/<screen>"`,
  `hostname: "app.kafkalens"`, `name`, `data`) and sets the distinct id to `install_id`
  (needs Umami ≥ 2.13 `id` support — **verify on the running instance**).
- Pros: already running, one dashboard for site + app, retention/funnel/journey reports built in,
  no new infra, ~zero cost.
- Cons: web-centric model (url/referrer/screen), event-data numeric analysis is weak in the UI
  → use SQL directly on Umami's Postgres (or attach Metabase) for setting distributions;
  Umami's bot filter checks User-Agent → send a browser-like UA containing `KafkaLens/<ver>`
  (verify); one Umami instance shared with greenfit traffic.
- Kill-switch: `GET t.kafkalens.com/config.json` (static file on VPS) → `{ enabled, sample_rate, min_version }`.

### Option B — Own tiny ingest + ClickHouse/Postgres + Metabase/Grafana

- ASP.NET minimal API (`/v1/events`, batch JSON), validates schema, drops IP after GeoIP→country.
- Storage: Postgres (fine for < ~10M events/month) or ClickHouse.
- Dashboards: Metabase (DAU/WAU/MAU, retention SQL) or Grafana.
- Pros: exact schema, proper typed columns, unlimited ad-hoc SQL, same stack as the app.
- Cons: build + operate ~1–2 days of backend work, dashboards from scratch, backups/monitoring.

### Option C — Hosted/self-hosted product analytics (PostHog / Aptabase)

- PostHog: richest (retention, funnels, feature flags) but self-host needs ~16 GB RAM
  (ClickHouse + Kafka); cloud free tier 1M events/month but data leaves your control.
- Aptabase: built for desktop apps, privacy-first, has a .NET SDK, self-hostable — but no
  persistent user id by design → weak DAU/MAU/retention, which are core asks here.

**Recommendation:** A for 1.0, behind `ITelemetrySink` + our own domain, so moving to B later
is a server-side change only. Revisit after ~4 weeks of data.

## 8. Website

Keep GitHub Pages — no need to move for this. Changes:
- Add `privacy.html` (event list, opt-out instructions, retention period), link in footer + app.
- Pass `utm_source` into downloads (already merged into URLs by `analytics.js`) — optionally
  write it into the Windows installer filename/registry to attribute `app_first_run` (phase 2).
- VPS only hosts `t.kafkalens.com` (proxy + `config.json`). DNS: CNAME/A record for `t`.

Move the site to the VPS only if we later need server-side logic (signed downloads, license, etc.).

## 9. Decisions needed

1. **Consent model**
   - (a) Opt-out with first-run notice (default on, clear dialog, one click to disable) — max data,
     common for dev tools (VS Code, .NET SDK, Homebrew).
   - (b) Opt-in — most trust, but typically 10–30% participation → biased, small sample.
   - Recommendation: (a), with `DO_NOT_TRACK` honoured and the public schema page.
2. **Backend**: A (recommended) vs B vs C.
3. **Retention period**: raw events 13 months (enough for YoY retention), then aggregate-only.
4. **Scope for 1.0**: lifecycle + fetch + settings snapshot + top features (§10 phase 1)
   vs full catalog.

## 10. Rollout

| Phase | Content | Est. |
|---|---|---|
| 0 | Approve this plan; verify Umami version/`id`/UA filter; DNS `t.kafkalens.com`, proxy, `config.json` | 0.5 d |
| 1 | `ITelemetry` + service + sink + spool + opt-out UI + first-run notice + lifecycle/heartbeat + `fetch_completed` + `settings_snapshot` + tests | 2–3 d |
| 2 | `feature_used` wiring across VMs, formatters, plugins, updates; "View telemetry data" dialog | 1–2 d |
| 3 | `privacy.html`, dashboards (Umami + SQL views: DAU/WAU/MAU, retention, settings distributions) | 1 d |
| 4 | 0.9.10 beta to self/early users, validate data, then 1.0 | — |

## 11. Impacted modules

`Shared` (interface + event types), `ViewModels` (service, sink, wiring, Preferences UI VM),
`KafkaLens.UI` (DI, activity tracker, first-run dialog view, Preferences view),
`ViewModels.Tests`, `docs/` (privacy page, this plan), VPS/DNS (outside repo).
No change to `Core`, `GrpcApi`, clients, or packaging.

## 11a. Implementation notes (as built)

- `Shared/Telemetry/ITelemetry.cs` — interface + `NullTelemetry`.
- `ViewModels/Telemetry/` — `TelemetryService` (channel → sender loop, consent gate, kill-switch,
  heartbeats, `SemaphoreSlim`-serialized flush), `TelemetrySpool`, `UmamiTelemetrySink`,
  `LogTelemetrySink` (Debug builds), `TelemetrySchema` (allow-list), `SettingsSnapshot`, `ErrorReport`,
  `ActivityTracker`, `TelemetryEnvironment`.
- Unexpected failures: `AppDomain.UnhandledException` (spooled synchronously, sent next launch),
  `TaskScheduler.UnobservedTaskException`; `previous_unclean_exit` on `app_started` catches native
  crashes / kills via a session marker file.
- UI: first-run notice (OK / Disable), Preferences → Privacy (toggle, install-id reset, events sent this session).
- Config: `appsettings.json` → `Telemetry` section. **`WebsiteId` is empty, so nothing is sent until it is set.**

### Ops checklist before 1.0

1. Umami: create website "KafkaLens App"; put its id in `appsettings.json` → `Telemetry.WebsiteId`.
2. Verify on the running Umami: version ≥ 2.13 (distinct `id`), payload `timestamp` accepted,
   UA `Mozilla/5.0 (...) KafkaLens/x.y.z` not filtered as a bot.
3. DNS `t.kafkalens.com` → VPS; reverse-proxy `/api/send` → Umami; serve `/config.json` = `{"enabled": true}`.
4. Umami data retention: 13 months.
5. Dashboards: DAU/WAU/MAU = distinct ids per day/week/month; time spent = sum `heartbeat.active_s`;
   crash rate = `app_error` with `kind=unhandled` ÷ `app_started`, grouped by `fingerprint` / `app_version`.
   The [desktop dashboard](../tools/analytics-dashboard/README.md) reports version adoption, feature use
   and operation latency from named events. The `duration_ms` property adds whole-millisecond timings
   to startup, fetch and topic-load events. Older startup and fetch buckets remain available.

## 12. Risks

- **Trust backlash** (dev-tool telemetry is sensitive) → strict allow-list, public schema,
  opt-out, DO_NOT_TRACK, no identifiers from Kafka, open-source code reviewable.
- **Corporate firewalls** block the endpoint → silent drop, capped spool, no retries storm.
- **Data skew**: opt-out users / air-gapped users are invisible; treat numbers as lower bounds.
- **Schema drift** → `schema` prop + typed helpers + test enumerating schema vs `privacy.html`.
- **Umami limits** → sink abstraction + own domain makes migration to B cheap.
