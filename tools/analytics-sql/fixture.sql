-- Minimal Umami 3.5-compatible PostgreSQL relations. Use only in a new test database.
CREATE TABLE public.session (
    session_id uuid PRIMARY KEY, website_id uuid NOT NULL, distinct_id varchar(50)
);
CREATE TABLE public.website_event (
    event_id uuid PRIMARY KEY, website_id uuid NOT NULL, session_id uuid NOT NULL REFERENCES public.session,
    created_at timestamptz, event_type integer NOT NULL DEFAULT 2, event_name varchar(50)
);
CREATE TABLE public.event_data (
    event_data_id uuid PRIMARY KEY, website_id uuid NOT NULL,
    website_event_id uuid NOT NULL REFERENCES public.website_event,
    created_at timestamptz, data_key varchar(500) NOT NULL, data_type integer NOT NULL,
    string_value varchar(500), number_value decimal(19, 4)
);
CREATE INDEX ON public.website_event (website_id, created_at, event_name);
CREATE INDEX ON public.event_data (website_event_id);

INSERT INTO public.session
SELECT md5(n::text)::uuid,
       CASE WHEN n = 5 THEN md5('other')::uuid ELSE 'cf83f60c-42eb-4829-bccf-e8281d54e66f'::uuid END,
       CASE n WHEN 1 THEN 'A' WHEN 2 THEN 'A' WHEN 3 THEN 'B' WHEN 5 THEN 'other-property' END
FROM generate_series(1, 5) AS n;

CREATE TEMP TABLE fixture_events (
    id integer, session_n integer, day integer, event_name text, properties jsonb
);
INSERT INTO fixture_events VALUES
    (1, 1, 1, 'app_started', '{"app_version":"1.0.0","schema":1,"session_id":"legacy","startup_ms":"<3s"}'),
    (2, 1, 1, 'fetch_completed', '{"schema":1,"session_id":"legacy","duration":"<1s","result":"ok","position":"time","direction":"forward"}'),
    (3, 1, 1, 'settings_snapshot', '{"app_version":"1.0.0","schema":1,"theme":"Dark","theme_is_default":false}'),
    (4, 2, 2, 'app_started', '{"app_version":"1.1.0","schema":2,"session_id":"new","seq":5,"duration_ms":20}'),
    (5, 2, 2, 'topics_available', '{"schema":2,"session_id":"new","seq":1,"source":"retry","launch_elapsed_ms":3}'),
    (6, 2, 2, 'fetch_completed', '{"schema":2,"session_id":"new","seq":2,"result":"failed","duration_ms":10,"launch_elapsed_ms":7}'),
    (7, 2, 2, 'fetch_completed', '{"schema":2,"session_id":"new","seq":3,"result":"superseded","duration_ms":10,"launch_elapsed_ms":9}'),
    (8, 2, 2, 'fetch_completed', '{"schema":2,"session_id":"new","seq":4,"result":"ok","duration_ms":200,"launch_elapsed_ms":17,"received":"0"}'),
    (9, 2, 2, 'fetch_completed', '{"schema":2,"session_id":"new","seq":4,"result":"ok","duration_ms":200,"launch_elapsed_ms":17,"received":"0"}'),
    (10, 2, 2, 'heartbeat', '{"app_version":"1.1.0","schema":2,"session_id":"new","seq":6,"active_s":100}'),
    (11, 2, 2, 'settings_snapshot', '{"app_version":"1.1.0","schema":2,"session_id":"new","seq":7,"theme":"System","theme_is_default":true}'),
    (12, 2, 2, 'feature_used', '{"schema":2,"session_id":"new","seq":8,"feature":"tab_open"}'),
    (13, 2, 2, 'cluster_connected', '{"schema":2,"session_id":"new","seq":9,"result":"ok","duration_ms":20,"attempts":2}'),
    (14, 3, 2, 'heartbeat', '{"app_version":"1.0.0","schema":1,"session_id":"old","active_s":50}'),
    (15, 5, 2, 'heartbeat', '{"app_version":"foreign","schema":1,"active_s":999}'),
    (16, 4, 2, 'feature_used', '{"feature":"tab_open"}'),
    (17, 2, 3, 'app_started', '{"app_version":"future","schema":2,"session_id":"future","seq":1}'),
    (18, 2, 2, 'fetch_completed', '{"schema":2,"session_id":"new","seq":10,"result":"ok","duration_ms":400,"launch_elapsed_ms":22,"received":"1"}');

INSERT INTO public.website_event (event_id, website_id, session_id, created_at, event_name)
SELECT md5('e' || f.id)::uuid, s.website_id, s.session_id,
       timestamptz '2026-01-01 00:00Z' + f.day * interval '1 day', f.event_name
FROM fixture_events AS f JOIN public.session AS s ON s.session_id = md5(f.session_n::text)::uuid;

INSERT INTO public.event_data
SELECT md5('p' || f.id || p.key)::uuid, e.website_id, e.event_id, e.created_at, p.key,
       CASE jsonb_typeof(p.value) WHEN 'number' THEN 2 WHEN 'boolean' THEN 3 ELSE 1 END,
       CASE WHEN jsonb_typeof(p.value) <> 'number' THEN p.value #>> '{}' END,
       CASE WHEN jsonb_typeof(p.value) = 'number' THEN (p.value #>> '{}')::numeric END
FROM fixture_events AS f JOIN public.website_event AS e ON e.event_id = md5('e' || f.id)::uuid
CROSS JOIN LATERAL jsonb_each(f.properties) AS p;
