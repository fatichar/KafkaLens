-- Run as a role administrator after views.sql.
BEGIN;
DO $$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'kafkalens_report_reader') THEN
        CREATE ROLE kafkalens_report_reader NOLOGIN;
    END IF;
END $$;
GRANT USAGE ON SCHEMA kafkalens_reporting TO kafkalens_report_reader;
GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA kafkalens_reporting TO kafkalens_report_reader;
COMMIT;
