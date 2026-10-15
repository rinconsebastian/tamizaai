-- Local dev kit: a sample download is prepared by the analytics worker (queued here, like a test) as one zip, which is kept
-- in the database in parts for an hour so the API can hand it to its requester through a single-use link. The API and the
-- worker share no disk; the database is where they meet.

CREATE TABLE tamiza.sample_downloads (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    requested_by uuid NOT NULL,
    sample_size integer NOT NULL,
    status character varying(16) NOT NULL,
    clean_version_id uuid,
    queued_at timestamp with time zone NOT NULL,
    started_at timestamp with time zone,
    finished_at timestamp with time zone,
    size_bytes bigint,
    file_sha256 character varying(64),
    contents jsonb,
    error_code character varying(64),
    error text,
    CONSTRAINT pk_sample_downloads PRIMARY KEY (id),
    CONSTRAINT ck_sample_downloads_status CHECK (status IN ('queued', 'running', 'succeeded', 'failed')),
    CONSTRAINT ck_sample_downloads_sample_size CHECK (sample_size BETWEEN 1 AND 5000),
    CONSTRAINT fk_sample_downloads_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_sample_downloads_users_requested_by FOREIGN KEY (requested_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    -- NO ACTION rather than RESTRICT: deleting a project cascades to both tables in one statement.
    CONSTRAINT fk_sample_downloads_script_versions_clean_version_id FOREIGN KEY (clean_version_id) REFERENCES tamiza.script_versions (id)
);

-- At most one queued download per user and project; a new request replaces the queued one.
CREATE UNIQUE INDEX ix_sample_downloads_project_id_requested_by_queued ON tamiza.sample_downloads (project_id, requested_by) WHERE status = 'queued';

CREATE INDEX ix_sample_downloads_status_queued_at ON tamiza.sample_downloads (status, queued_at);

CREATE INDEX ix_sample_downloads_finished_at ON tamiza.sample_downloads (finished_at);

CREATE INDEX ix_sample_downloads_requested_by ON tamiza.sample_downloads (requested_by);

CREATE INDEX ix_sample_downloads_clean_version_id ON tamiza.sample_downloads (clean_version_id);

-- The zip, in parts of 1 MiB written in order, so neither the worker nor the API holds the whole file in memory.
CREATE TABLE tamiza.sample_download_parts (
    download_id uuid NOT NULL,
    part integer NOT NULL,
    data bytea NOT NULL,
    CONSTRAINT pk_sample_download_parts PRIMARY KEY (download_id, part),
    CONSTRAINT fk_sample_download_parts_sample_downloads_download_id FOREIGN KEY (download_id) REFERENCES tamiza.sample_downloads (id) ON DELETE CASCADE
);

-- A zip does not compress further, so PostgreSQL should not try.
ALTER TABLE tamiza.sample_download_parts ALTER COLUMN data SET STORAGE EXTERNAL;
