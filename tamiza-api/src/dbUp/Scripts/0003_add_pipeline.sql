-- Python pipeline: script versions, runs (the analytics worker's queue) and their steps; pipeline settings and the
-- form's choice lists on projects.

ALTER TABLE tamiza.projects
    ADD COLUMN run_after_sync boolean NOT NULL DEFAULT TRUE,
    ADD COLUMN run_interval_hours integer,
    ADD COLUMN next_run_at timestamp with time zone,
    ADD COLUMN form_choices jsonb NOT NULL DEFAULT '[]'::jsonb,
    ADD CONSTRAINT ck_projects_run_interval_hours CHECK (run_interval_hours BETWEEN 1 AND 168);

CREATE TABLE tamiza.script_versions (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    kind character varying(16) NOT NULL,
    number integer NOT NULL,
    code text,
    author_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_script_versions PRIMARY KEY (id),
    CONSTRAINT ck_script_versions_kind CHECK (kind IN ('clean', 'output', 'indicators')),
    CONSTRAINT fk_script_versions_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_script_versions_users_author_id FOREIGN KEY (author_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ix_script_versions_project_id_kind_number ON tamiza.script_versions (project_id, kind, number);

CREATE INDEX ix_script_versions_author_id ON tamiza.script_versions (author_id);

CREATE TABLE tamiza.runs (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    trigger character varying(16) NOT NULL,
    status character varying(16) NOT NULL,
    requested_by uuid,
    clean_version_id uuid,
    output_version_id uuid,
    indicators_version_id uuid,
    queued_at timestamp with time zone NOT NULL,
    started_at timestamp with time zone,
    finished_at timestamp with time zone,
    error_code character varying(64),
    error text,
    CONSTRAINT pk_runs PRIMARY KEY (id),
    CONSTRAINT ck_runs_trigger CHECK (trigger IN ('manual', 'sync', 'schedule')),
    CONSTRAINT ck_runs_status CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
    CONSTRAINT fk_runs_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_runs_users_requested_by FOREIGN KEY (requested_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    -- NO ACTION rather than RESTRICT: deleting a project cascades to both tables in one statement.
    CONSTRAINT fk_runs_script_versions_clean_version_id FOREIGN KEY (clean_version_id) REFERENCES tamiza.script_versions (id),
    CONSTRAINT fk_runs_script_versions_output_version_id FOREIGN KEY (output_version_id) REFERENCES tamiza.script_versions (id),
    CONSTRAINT fk_runs_script_versions_indicators_version_id FOREIGN KEY (indicators_version_id) REFERENCES tamiza.script_versions (id)
);

-- At most one queued run per project; a running run may have one queued behind it.
CREATE UNIQUE INDEX ix_runs_project_id_queued ON tamiza.runs (project_id) WHERE status = 'queued';

CREATE INDEX ix_runs_project_id_queued_at ON tamiza.runs (project_id, queued_at DESC);

CREATE INDEX ix_runs_requested_by ON tamiza.runs (requested_by);

CREATE INDEX ix_runs_clean_version_id ON tamiza.runs (clean_version_id);

CREATE INDEX ix_runs_output_version_id ON tamiza.runs (output_version_id);

CREATE INDEX ix_runs_indicators_version_id ON tamiza.runs (indicators_version_id);

CREATE TABLE tamiza.run_steps (
    run_id uuid NOT NULL,
    step character varying(16) NOT NULL,
    status character varying(16) NOT NULL,
    started_at timestamp with time zone,
    finished_at timestamp with time zone,
    input_rows bigint,
    output_rows bigint,
    tables jsonb NOT NULL DEFAULT '[]'::jsonb,
    log text NOT NULL DEFAULT '',
    error text,
    CONSTRAINT pk_run_steps PRIMARY KEY (run_id, step),
    CONSTRAINT ck_run_steps_step CHECK (step IN ('flatten', 'clean', 'output', 'indicators')),
    CONSTRAINT ck_run_steps_status CHECK (status IN ('succeeded', 'failed', 'skipped')),
    CONSTRAINT fk_run_steps_runs_run_id FOREIGN KEY (run_id) REFERENCES tamiza.runs (id) ON DELETE CASCADE
);
