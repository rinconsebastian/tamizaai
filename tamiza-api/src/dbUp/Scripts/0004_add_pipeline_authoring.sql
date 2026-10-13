-- Pipeline authoring: sample test runs of a draft script (executed by the analytics worker ahead of queued runs) and
-- project reference tables that scripts read through ctx.reference; runs record the reference tables they used.

CREATE TABLE tamiza.script_tests (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    requested_by uuid NOT NULL,
    kind character varying(16) NOT NULL,
    code text NOT NULL,
    sample_size integer NOT NULL,
    status character varying(16) NOT NULL,
    clean_version_id uuid,
    queued_at timestamp with time zone NOT NULL,
    started_at timestamp with time zone,
    finished_at timestamp with time zone,
    error_code character varying(64),
    error text,
    steps jsonb NOT NULL DEFAULT '[]'::jsonb,
    CONSTRAINT pk_script_tests PRIMARY KEY (id),
    CONSTRAINT ck_script_tests_kind CHECK (kind IN ('clean', 'output', 'indicators')),
    CONSTRAINT ck_script_tests_status CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
    CONSTRAINT ck_script_tests_sample_size CHECK (sample_size BETWEEN 1 AND 5000),
    CONSTRAINT fk_script_tests_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_script_tests_users_requested_by FOREIGN KEY (requested_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    -- NO ACTION rather than RESTRICT: deleting a project cascades to both tables in one statement.
    CONSTRAINT fk_script_tests_script_versions_clean_version_id FOREIGN KEY (clean_version_id) REFERENCES tamiza.script_versions (id)
);

-- At most one queued test per user and project; a new test replaces the queued one.
CREATE UNIQUE INDEX ix_script_tests_project_id_requested_by_queued ON tamiza.script_tests (project_id, requested_by) WHERE status = 'queued';

CREATE INDEX ix_script_tests_status_queued_at ON tamiza.script_tests (status, queued_at);

CREATE INDEX ix_script_tests_finished_at ON tamiza.script_tests (finished_at);

CREATE INDEX ix_script_tests_requested_by ON tamiza.script_tests (requested_by);

CREATE INDEX ix_script_tests_clean_version_id ON tamiza.script_tests (clean_version_id);

CREATE TABLE tamiza.reference_tables (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    name character varying(63) NOT NULL,
    file_name character varying(255) NOT NULL,
    columns jsonb NOT NULL,
    row_count integer NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_reference_tables PRIMARY KEY (id),
    CONSTRAINT ck_reference_tables_name CHECK (name ~ '^[a-z][a-z0-9_]{0,62}$'),
    CONSTRAINT fk_reference_tables_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_reference_tables_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ix_reference_tables_project_id_name ON tamiza.reference_tables (project_id, name);

CREATE INDEX ix_reference_tables_updated_by ON tamiza.reference_tables (updated_by);

-- The rows, apart from the metadata so listing tables never reads them: a JSON array of rows, each an array of values
-- in column order.
CREATE TABLE tamiza.reference_table_contents (
    reference_table_id uuid NOT NULL,
    rows jsonb NOT NULL,
    CONSTRAINT pk_reference_table_contents PRIMARY KEY (reference_table_id),
    CONSTRAINT fk_reference_table_contents_reference_tables_reference_table_id FOREIGN KEY (reference_table_id) REFERENCES tamiza.reference_tables (id) ON DELETE CASCADE
);

ALTER TABLE tamiza.runs
    ADD COLUMN reference_tables jsonb NOT NULL DEFAULT '[]'::jsonb;
