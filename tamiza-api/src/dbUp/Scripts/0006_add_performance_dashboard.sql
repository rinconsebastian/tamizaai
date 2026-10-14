-- Performance dashboard: the time zone that defines a project's days, and an index for the dashboard's date range and
-- daily counts, which read the live submissions of one project by the time Kobo received them.

ALTER TABLE tamiza.projects
    ADD COLUMN time_zone character varying(64) NOT NULL DEFAULT 'UTC';

CREATE INDEX ix_raw_submissions_project_id_submitted_at ON tamiza.raw_submissions (project_id, submitted_at) WHERE NOT deleted;
