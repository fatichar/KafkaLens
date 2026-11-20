SET ROLE kafkalens_report_reader;
DO $$
DECLARE
    start_at timestamptz := '2026-01-02 00:00Z';
    end_at timestamptz := '2026-01-04 00:00Z';
    metrics record;
BEGIN
    IF has_schema_privilege(current_user, 'kafkalens_private', 'USAGE')
       OR has_table_privilege(current_user, 'public.event_data', 'SELECT') THEN
        RAISE EXCEPTION 'Reporting role has raw-event access';
    END IF;

    SELECT sum(installations) AS installs, sum(active_seconds) AS seconds,
           count(*) FILTER (WHERE app_version = '1.1.0' AND installations = 1) AS upgrades
    INTO metrics FROM kafkalens_reporting.versions(start_at, end_at);
    IF (metrics.installs, metrics.seconds, metrics.upgrades) IS DISTINCT FROM (2::numeric, 150::numeric, 1::bigint) THEN
        RAISE EXCEPTION 'Unexpected adoption: %', metrics;
    END IF;

    SELECT * INTO STRICT metrics FROM kafkalens_reporting.features(start_at, end_at) WHERE feature = 'tab_open';
    IF (metrics.uses, metrics.installations, metrics.reach_pct) IS DISTINCT FROM (1::bigint, 1::bigint, 50::numeric) THEN
        RAISE EXCEPTION 'Unexpected feature reach: %', metrics;
    END IF;

    SELECT * INTO STRICT metrics FROM kafkalens_reporting.settings_coverage(start_at, end_at);
    IF metrics.coverage_pct IS DISTINCT FROM 50::numeric THEN
        RAISE EXCEPTION 'Unexpected coverage: %', metrics;
    END IF;
    SELECT * INTO STRICT metrics FROM kafkalens_reporting.settings(start_at, end_at) WHERE setting = 'theme';
    IF (metrics.value, metrics.uses_default, metrics.app_version, metrics.active_seconds)
       IS DISTINCT FROM ('System'::text, true, '1.1.0'::text, 100::numeric) THEN
        RAISE EXCEPTION 'Unexpected settings: %', metrics;
    END IF;

    SELECT * INTO STRICT metrics FROM kafkalens_reporting.settings_defaults(start_at, end_at) WHERE setting = 'theme';
    IF metrics.default_pct IS DISTINCT FROM 100::numeric THEN
        RAISE EXCEPTION 'Unexpected default use: %', metrics;
    END IF;

    SELECT * INTO STRICT metrics FROM kafkalens_reporting.flow(start_at, end_at);
    IF (metrics.launches, metrics.topics_available, metrics.successful_fetch, metrics.first_fetch_empty,
        metrics.fetch_failed, metrics.recovered_after_failure, metrics.timed_fetches, metrics.p50_first_fetch_ms)
       IS DISTINCT FROM (1::bigint, 1::bigint, 1::bigint, 1::bigint, 1::bigint, 1::bigint, 1::bigint, 17::numeric) THEN
        RAISE EXCEPTION 'Unexpected flow: %', metrics;
    END IF;

    SELECT * INTO STRICT metrics FROM kafkalens_reporting.latency(start_at, end_at)
    WHERE operation = 'fetch_completed' AND outcome = 'ok' AND historical_bucket IS NULL;
    IF (metrics.events, metrics.samples, metrics.p50_ms, metrics.p95_ms)
       IS DISTINCT FROM (2::bigint, 2::bigint, 300::numeric, 390::numeric) THEN
        RAISE EXCEPTION 'Duplicate or missing timing: %', metrics;
    END IF;
    SELECT * INTO STRICT metrics FROM kafkalens_reporting.latency(start_at, end_at)
    WHERE operation = 'fetch_completed' AND historical_bucket = '<1s';
    IF metrics.samples <> 0 OR metrics.p95_ms IS NOT NULL THEN
        RAISE EXCEPTION 'Bucket included in numeric timings: %', metrics;
    END IF;
    SELECT * INTO STRICT metrics FROM kafkalens_reporting.latency(start_at, end_at)
    WHERE operation = 'cluster_connected';
    IF metrics.retried <> 1 THEN RAISE EXCEPTION 'Retry count missing'; END IF;

    SELECT * INTO STRICT metrics FROM kafkalens_reporting.fetch_modes(start_at, end_at)
    WHERE fetch_position = 'time' AND fetch_direction = 'forward';
    IF metrics.fetches <> 1 THEN RAISE EXCEPTION 'Unexpected fetch modes'; END IF;
END $$;
RESET ROLE;
