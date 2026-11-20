# KafkaLens desktop reporting

Umami collects the desktop events. PostgreSQL aggregates them in the Umami database.
Metabase runs the reports under its own login and receives grouped data only. The
desktop client continues sending named events, so the Umami pageview Overview is
independent of this dashboard.

## Database setup

Use a backup or staging database to verify compatibility before applying this against
the KafkaLens App property on the VPS. The adapter in `views.sql` targets the
[Umami 3.5 PostgreSQL schema](https://github.com/umami-software/umami/blob/v3.5.0/prisma/schema.prisma)
and filters to the KafkaLens App property UUID. Pin the Umami image version in the
existing deployment and retest the adapter when upgrading it.

1. Create a dedicated, non-superuser schema owner and grant it `CONNECT` and
   `CREATE` on the Umami database, `USAGE` on `public`, and `SELECT` on
   `public.website_event`, `public.event_data` and `public.session`. Apply
   `psql -X -v ON_ERROR_STOP=1 -f tools/analytics-sql/views.sql` while connected
   as that owner. Preserve that owner for subsequent migrations.
2. As the database role administrator, apply
   `psql -X -v ON_ERROR_STOP=1 -f tools/analytics-sql/grant.sql`, then provision
   a dedicated Metabase data-source login. Grant it `CONNECT` on the Umami database
   and membership in `kafkalens_report_reader`. Give it no raw table or private
   schema grants. Use a unique password and set a per-role statement timeout for
   dashboard queries (for example 20 seconds).
3. In a new test database, run the fixture checks before using a new Umami version.

   ```sh
   psql -X -v ON_ERROR_STOP=1 -d kafkalens_reporting_test \
     -f tools/analytics-sql/fixture.sql -f tools/analytics-sql/views.sql \
     -f tools/analytics-sql/grant.sql -f tools/analytics-sql/checks.sql
   ```

The fixture checks use `SET ROLE` to verify that reports work without raw-event access.
Before upgrading a live instance, also apply the adapter against a disposable copy of
the actual Umami database schema and verify these aggregates on a known period.

## Run Metabase

Provision a separate `metabase` database and metadata owner for Metabase's own users,
dashboards and saved questions. The metadata owner and the Umami data-source login
are separate roles. In a protected env file outside the checkout, set
`METABASE_IMAGE` to a pinned Metabase release tag or digest, `METABASE_SITE_URL` to the
private HTTPS dashboard URL, `METABASE_DB_HOST`, `METABASE_DB_USER`,
`METABASE_DB_PASSWORD`, `METABASE_ENCRYPTION_KEY`, and `REPORTING_NETWORK` to the name
of the database's private Docker network. Generate the encryption key with
`openssl rand -hex 32` and retain it with the metadata backup. Defaults are port
`3130` on the VPS loopback interface, metadata DB `metabase`, and DB port `5432`.

```sh
docker compose --env-file /etc/kafkalens/metabase.env \
  -f tools/analytics-sql/compose.yaml up -d
```

Reach Metabase through a trusted SSH tunnel for initial admin setup, then expose it
through the HTTPS reverse proxy only to authorized staff. Keep public sharing disabled.
Add the Umami PostgreSQL database as a Metabase data source with the read-only login
from step 2.

Create a native SQL question for each report below. Set `days` to a Number variable
with a default of 30 and connect it to the dashboard's date-range control. Use a value
from 1 to 90. This example counts distinct installations by version.

```sql
SELECT *
FROM kafkalens_reporting.versions(
    now() - make_interval(hours => 24 * CAST({{days}} AS integer)), now()
)
ORDER BY installations DESC, app_version;
```

Use the same date arguments for these questions.

| Function | What to show |
| --- | --- |
| `versions` | Reporting installs by latest launch or heartbeat version, plus active seconds from heartbeats |
| `features` | Uses, distinct installations, and reach among installations with a launch or heartbeat |
| `settings_coverage` | Active installs with a settings snapshot in the window |
| `settings` | Latest snapshot per active install, with value counts and activity weighted by heartbeat seconds |
| `settings_defaults` | Default-use rate by setting and snapshot version, among installs that supplied that flag |
| `latency` | Outcomes, numeric sample counts, p50/p95, retries and historical buckets |
| `fetch_modes` | Actual fetch positions and directions by outcome |
| `flow` | Launches, topics available, first successful fetch, successful empty results and recovery |

For settings, interpret the activity-weighted values as the activity of installs that
last reported each setting. The data cannot show which value was configured during
every earlier heartbeat. Each field has its own coverage. Compare defaults within the
version that reported the snapshot.

Flow uses the launch ID and sequence, including when a restored tab has topics before
startup reporting finishes. Counts cover launches with schema-2 startup events in the
selected window, with steps observed within that window. A successful empty fetch
counts as a successful operation and has its own total. Recovery means a later
success in the same launch. The telemetry does not link a retry to a specific topic.
Time to first fetch uses monotonic elapsed time from launch telemetry initialization.

Historical timing buckets remain separate from numeric percentiles. Topic timing uses
the successful or final request's duration and counts attempts independently. Cached
and shared waits do not generate additional latency samples. The first reports can use
existing event data for adoption, feature use, settings and historical buckets. The
flow report and numeric percentiles need schema-2 clients.

At current volume the database aggregates on demand. Check query plans on a
representative 90-day window before enabling scheduled refreshes. If volume warrants
precomputation, keep the report signatures and replace the underlying queries with
rollups. Keep raw events and installation IDs on the database side.
