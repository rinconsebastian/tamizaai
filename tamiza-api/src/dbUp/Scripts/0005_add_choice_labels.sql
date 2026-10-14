-- Choice labels: the definition (questions and choice lists) of every form version the project's submissions were made
-- with, so the flatten step can translate each answer with the labels of its own version. Rows are recorded when a
-- submission with a new version is stored and fetched from Kobo later; the version the project's snapshot describes is
-- kept in projects.form_version.

CREATE TABLE tamiza.form_versions (
    project_id uuid NOT NULL,
    uid character varying(128) NOT NULL,
    status character varying(16) NOT NULL,
    fields jsonb NOT NULL DEFAULT '[]'::jsonb,
    choices jsonb NOT NULL DEFAULT '[]'::jsonb,
    deployed_at timestamp with time zone,
    recorded_at timestamp with time zone NOT NULL,
    fetched_at timestamp with time zone,
    attempts integer NOT NULL DEFAULT 0,
    next_attempt_at timestamp with time zone,
    error_code character varying(64),
    error text,
    CONSTRAINT pk_form_versions PRIMARY KEY (project_id, uid),
    CONSTRAINT ck_form_versions_status CHECK (status IN ('pending', 'available', 'failed', 'not_found')),
    CONSTRAINT fk_form_versions_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE
);

-- The background pass looks for versions that are due.
CREATE INDEX ix_form_versions_next_attempt_at ON tamiza.form_versions (next_attempt_at) WHERE status IN ('pending', 'failed');

ALTER TABLE tamiza.projects
    ADD COLUMN form_version character varying(128);

-- Per-version submission counts and the re-recording of versions done at each synchronization.
CREATE INDEX ix_raw_submissions_project_id_form_version ON tamiza.raw_submissions (project_id, form_version) WHERE NOT deleted;

-- The versions of the stored submissions, to be fetched by the background pass as soon as the API starts.
INSERT INTO tamiza.form_versions (project_id, uid, status, recorded_at, next_attempt_at)
SELECT DISTINCT project_id, form_version, 'pending', now(), now()
FROM tamiza.raw_submissions
WHERE form_version IS NOT NULL AND NOT deleted;
