BEGIN;

CREATE SCHEMA IF NOT EXISTS kafkalens_private;
CREATE SCHEMA IF NOT EXISTS kafkalens_reporting;
REVOKE ALL ON SCHEMA kafkalens_private, kafkalens_reporting FROM PUBLIC;

-- The Umami 3.5 PostgreSQL adapter. Keep the property and raw-table access here.
-- Apply only in KafkaLens' reporting database or the Umami database that owns this property.
CREATE OR REPLACE VIEW kafkalens_private.desktop_events AS
SELECT e.event_id, e.created_at, e.event_name,
       nullif(s.distinct_id, '') AS install_id, p.properties
FROM public.website_event AS e
JOIN public.session AS s
  ON s.session_id = e.session_id AND s.website_id = e.website_id
CROSS JOIN LATERAL (
    SELECT jsonb_object_agg(d.data_key, CASE d.data_type
               WHEN 2 THEN to_jsonb(d.number_value)
               ELSE to_jsonb(d.string_value)
           END ORDER BY d.event_data_id) AS properties
    FROM public.event_data AS d
    WHERE d.website_event_id = e.event_id AND d.website_id = e.website_id
      AND d.data_key IN (
          'app_version', 'schema', 'session_id', 'seq', 'launch_elapsed_ms',
          'active_s', 'feature', 'result', 'received', 'duration_ms', 'attempts', 'startup_ms', 'duration',
          'position', 'direction', 'theme', 'theme_is_default', 'default_fetch_count', 'default_fetch_count_is_default',
          'restore_tabs_on_startup', 'restore_tabs_on_startup_is_default', 'eager_load_topics',
          'eager_load_topics_is_default', 'auto_check_updates', 'auto_check_updates_is_default',
          'connection_check_interval_s', 'connection_check_interval_s_is_default'
      )
) AS p
WHERE e.website_id = 'cf83f60c-42eb-4829-bccf-e8281d54e66f'::uuid
  AND e.event_type = 2
  AND e.event_name IN ('app_started', 'heartbeat', 'feature_used', 'settings_snapshot',
                       'topics_available', 'cluster_connected', 'fetch_completed');

CREATE OR REPLACE FUNCTION kafkalens_private.events(p_start timestamptz, p_end timestamptz, p_names text[])
RETURNS TABLE (event_id uuid, created_at timestamptz, event_name text, install_id text, properties jsonb)
LANGUAGE sql STABLE SET search_path = pg_catalog, pg_temp
AS $$
    -- Sequence and launch ID deduplicate v2 spooled retries. Keep separate v1 rows.
    SELECT DISTINCT ON (dedup_key) event_id, created_at, event_name, install_id, properties
    FROM (
        SELECT *, CASE WHEN nullif(properties->>'session_id', '') IS NOT NULL
                             AND jsonb_typeof(properties->'seq') = 'number'
                       THEN concat(properties->>'session_id', ':', properties->>'seq')
                       ELSE event_id::text END AS dedup_key
        FROM kafkalens_private.desktop_events
        WHERE created_at >= p_start AND created_at < p_end
          AND p_end > p_start AND p_end - p_start <= interval '90 days'
          AND event_name = ANY(p_names)
    ) AS scoped
    ORDER BY dedup_key, created_at, event_id
$$;

CREATE OR REPLACE FUNCTION kafkalens_private.active(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (install_id text, app_version text, active_seconds numeric)
LANGUAGE sql STABLE SET search_path = pg_catalog, pg_temp
AS $$
    WITH activity AS MATERIALIZED (
        SELECT * FROM kafkalens_private.events(p_start, p_end, ARRAY['app_started', 'heartbeat'])
        WHERE install_id IS NOT NULL
    ), latest AS (
        SELECT DISTINCT ON (install_id) install_id, properties->>'app_version' AS app_version
        FROM activity
        ORDER BY install_id, created_at DESC, properties->>'session_id' DESC,
                 (properties->'seq') DESC NULLS LAST, event_id DESC
    ), seconds AS (
        SELECT install_id, sum((properties->>'active_s')::numeric) AS active_seconds
        FROM activity WHERE event_name = 'heartbeat' AND jsonb_typeof(properties->'active_s') = 'number'
        GROUP BY install_id
    )
    SELECT latest.install_id, coalesce(nullif(latest.app_version, ''), 'Unknown'), coalesce(seconds.active_seconds, 0)
    FROM latest LEFT JOIN seconds USING (install_id)
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.versions(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (app_version text, installations bigint, active_seconds numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    SELECT a.app_version, count(*), sum(a.active_seconds)
    FROM kafkalens_private.active(p_start, p_end) AS a GROUP BY a.app_version
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.features(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (feature text, uses bigint, installations bigint, active_installations bigint, reach_pct numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    WITH active AS MATERIALIZED (SELECT install_id FROM kafkalens_private.active(p_start, p_end)),
    counts AS (
        SELECT e.properties->>'feature' AS feature, count(*) AS uses, count(DISTINCT e.install_id) AS installations
        FROM kafkalens_private.events(p_start, p_end, ARRAY['feature_used']) AS e
        JOIN active USING (install_id)
        WHERE nullif(e.properties->>'feature', '') IS NOT NULL
        GROUP BY e.properties->>'feature'
    )
    SELECT c.feature, c.uses, c.installations, a.total, round(100.0 * c.installations / nullif(a.total, 0), 2)
    FROM counts AS c CROSS JOIN (SELECT count(*) AS total FROM active) AS a
$$;

CREATE OR REPLACE FUNCTION kafkalens_private.install_settings(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (app_version text, snapshot jsonb, active_seconds numeric)
LANGUAGE sql STABLE SET search_path = pg_catalog, pg_temp
AS $$
    WITH latest AS (
        SELECT DISTINCT ON (install_id) install_id, properties
        FROM kafkalens_private.events(p_start, p_end, ARRAY['settings_snapshot'])
        WHERE install_id IS NOT NULL
        ORDER BY install_id, created_at DESC, properties->>'session_id' DESC,
                 (properties->'seq') DESC NULLS LAST, event_id DESC
    )
    SELECT coalesce(nullif(latest.properties->>'app_version', ''), a.app_version),
           latest.properties, a.active_seconds
    FROM kafkalens_private.active(p_start, p_end) AS a LEFT JOIN latest USING (install_id)
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.settings_coverage(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (active_installations bigint, installations_with_snapshot bigint, coverage_pct numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    SELECT count(*), count(snapshot), round(100.0 * count(snapshot) / nullif(count(*), 0), 2)
    FROM kafkalens_private.install_settings(p_start, p_end)
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.settings(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (app_version text, setting text, value text, uses_default boolean, installations bigint,
               snapshots_with_field bigint, active_seconds numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    WITH counts AS (
        SELECT s.app_version, fields.setting, s.snapshot->>fields.setting AS value,
               CASE lower(s.snapshot->>(fields.setting || '_is_default'))
                   WHEN 'true' THEN true WHEN 'false' THEN false
                   WHEN '1' THEN true WHEN '0' THEN false
               END AS uses_default, count(*) AS installations, sum(s.active_seconds) AS active_seconds
        FROM kafkalens_private.install_settings(p_start, p_end) AS s
        CROSS JOIN (VALUES ('theme'), ('default_fetch_count'), ('restore_tabs_on_startup'),
                           ('eager_load_topics'), ('auto_check_updates'), ('connection_check_interval_s')) AS fields(setting)
        WHERE s.snapshot->>fields.setting IS NOT NULL
        GROUP BY 1, 2, 3, 4
    )
    SELECT app_version, setting, value, uses_default, installations,
           sum(installations) OVER (PARTITION BY app_version, setting)::bigint, active_seconds
    FROM counts
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.settings_defaults(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (app_version text, setting text, snapshots_with_field numeric, snapshots_with_default_flag numeric,
               installations_using_default numeric, default_pct numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    SELECT s.app_version, s.setting, sum(s.installations),
           coalesce(sum(s.installations) FILTER (WHERE s.uses_default IS NOT NULL), 0),
           coalesce(sum(s.installations) FILTER (WHERE s.uses_default), 0),
           round(100.0 * coalesce(sum(s.installations) FILTER (WHERE s.uses_default), 0)
                 / nullif(sum(s.installations) FILTER (WHERE s.uses_default IS NOT NULL), 0), 2)
    FROM kafkalens_reporting.settings(p_start, p_end) AS s GROUP BY s.app_version, s.setting
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.latency(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (operation text, outcome text, events bigint, samples bigint, p50_ms numeric, p95_ms numeric,
               retried bigint, historical_bucket text)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    WITH measurements AS (
        SELECT event_name, properties->>'result' AS result,
               CASE WHEN jsonb_typeof(properties->'duration_ms') = 'number'
                      AND (properties->>'duration_ms')::numeric BETWEEN 0 AND 600000
                    THEN (properties->>'duration_ms')::numeric END AS milliseconds,
               CASE WHEN jsonb_typeof(properties->'attempts') = 'number'
                    THEN (properties->>'attempts')::numeric > 1 END AS retry,
               CASE event_name WHEN 'app_started' THEN properties->>'startup_ms'
                               WHEN 'fetch_completed' THEN properties->>'duration' END AS bucket
        FROM kafkalens_private.events(p_start, p_end, ARRAY['app_started', 'fetch_completed', 'cluster_connected'])
    )
    SELECT event_name, coalesce(result, CASE WHEN event_name = 'app_started' THEN 'ok' ELSE 'Unknown' END),
           count(*), count(milliseconds),
           round((percentile_cont(0.5) WITHIN GROUP (ORDER BY milliseconds))::numeric, 1),
           round((percentile_cont(0.95) WITHIN GROUP (ORDER BY milliseconds))::numeric, 1),
           count(*) FILTER (WHERE retry), CASE WHEN milliseconds IS NULL THEN bucket END
    FROM measurements GROUP BY 1, 2, 8
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.fetch_modes(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (fetch_position text, fetch_direction text, outcome text, fetches bigint)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    SELECT coalesce(properties->>'position', 'Unknown'), coalesce(properties->>'direction', 'Unknown'),
           coalesce(properties->>'result', 'Unknown'), count(*)
    FROM kafkalens_private.events(p_start, p_end, ARRAY['fetch_completed'])
    GROUP BY 1, 2, 3
$$;

CREATE OR REPLACE FUNCTION kafkalens_reporting.flow(p_start timestamptz, p_end timestamptz)
RETURNS TABLE (launches bigint, topics_available bigint, successful_fetch bigint, first_fetch_empty bigint,
               fetch_failed bigint, recovered_after_failure bigint, timed_fetches bigint, p50_first_fetch_ms numeric)
LANGUAGE sql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp
AS $$
    WITH events AS MATERIALIZED (
        SELECT properties->>'session_id' AS launch_id, event_name,
               (properties->>'seq')::numeric AS seq, properties->>'result' AS outcome,
               properties->>'received' AS received,
               CASE WHEN jsonb_typeof(properties->'launch_elapsed_ms') = 'number'
                    THEN (properties->>'launch_elapsed_ms')::numeric END AS elapsed
        FROM kafkalens_private.events(p_start, p_end, ARRAY['app_started', 'topics_available', 'fetch_completed'])
        WHERE jsonb_typeof(properties->'seq') = 'number' AND nullif(properties->>'session_id', '') IS NOT NULL
    ), launches AS (
        SELECT launch_id, min(seq) FILTER (WHERE event_name = 'topics_available') AS topics_seq
        FROM events GROUP BY launch_id HAVING bool_or(event_name = 'app_started')
    ), fetches AS (
        SELECT e.*, l.topics_seq FROM events AS e JOIN launches AS l USING (launch_id)
        WHERE e.event_name = 'fetch_completed' AND e.seq > l.topics_seq
    ), first_success AS (
        SELECT DISTINCT ON (launch_id) launch_id, seq, received, elapsed
        FROM fetches WHERE outcome = 'ok' ORDER BY launch_id, seq
    ), first_failure AS (
        SELECT launch_id, min(seq) AS seq FROM fetches
        WHERE outcome NOT IN ('ok', 'cancelled', 'superseded') GROUP BY launch_id
    ), recovery AS (
        SELECT DISTINCT f.launch_id FROM fetches AS f JOIN first_failure AS bad USING (launch_id)
        WHERE f.outcome = 'ok' AND f.seq > bad.seq
    )
    SELECT count(*), count(l.topics_seq), count(ok.launch_id), count(*) FILTER (WHERE ok.received = '0'),
           count(bad.launch_id), count(r.launch_id), count(ok.elapsed),
           round((percentile_cont(0.5) WITHIN GROUP (ORDER BY ok.elapsed))::numeric, 1)
    FROM launches AS l LEFT JOIN first_success AS ok USING (launch_id)
    LEFT JOIN first_failure AS bad USING (launch_id) LEFT JOIN recovery AS r USING (launch_id)
$$;

REVOKE ALL ON ALL FUNCTIONS IN SCHEMA kafkalens_private, kafkalens_reporting FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA kafkalens_private, kafkalens_reporting FROM PUBLIC;
COMMIT;
