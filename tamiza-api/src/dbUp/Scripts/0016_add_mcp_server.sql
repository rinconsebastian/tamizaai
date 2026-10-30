-- MCP server for AI clients. Each project says how much of its data an AI client may read: off for every project, new and existing,
-- until an admin allows summaries or rows. Script versions record where they were saved (the editor or an AI client). Each tool call of
-- an AI client is recorded without its arguments and its result, and kept for a limited time; a record goes with its project.

ALTER TABLE tamiza.projects ADD COLUMN mcp_access character varying(16) NOT NULL DEFAULT 'off';
ALTER TABLE tamiza.projects ADD CONSTRAINT ck_projects_mcp_access CHECK (mcp_access IN ('off', 'aggregates', 'rows'));

ALTER TABLE tamiza.script_versions ADD COLUMN origin character varying(8) NOT NULL DEFAULT 'app';
ALTER TABLE tamiza.script_versions ADD CONSTRAINT ck_script_versions_origin CHECK (origin IN ('app', 'mcp'));

CREATE TABLE tamiza.mcp_calls (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    project_id uuid,
    tool character varying(64) NOT NULL,
    access character varying(16),
    status character varying(16) NOT NULL,
    error_code character varying(64),
    duration_ms integer NOT NULL,
    result_chars integer,
    started_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_mcp_calls PRIMARY KEY (id),
    CONSTRAINT fk_mcp_calls_users_user_id FOREIGN KEY (user_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_mcp_calls_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT ck_mcp_calls_access CHECK (access IS NULL OR access IN ('aggregates', 'rows')),
    CONSTRAINT ck_mcp_calls_status CHECK (status IN ('succeeded', 'failed', 'refused'))
);

CREATE INDEX ix_mcp_calls_project_id_started_at ON tamiza.mcp_calls (project_id, started_at DESC);
CREATE INDEX ix_mcp_calls_user_id_started_at ON tamiza.mcp_calls (user_id, started_at);
CREATE INDEX ix_mcp_calls_started_at ON tamiza.mcp_calls (started_at);
