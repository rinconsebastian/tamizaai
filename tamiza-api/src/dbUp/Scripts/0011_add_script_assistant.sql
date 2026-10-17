-- Script assistant: one row per request to the AI assistant. The rows hold who asked, for which script, the model, the token
-- counts, the duration, the outcome and the size of each part of the context, but never a message, the editor's text, the
-- context or the answer. The daily token budget and the one-request-at-a-time rule are enforced from them.

CREATE TABLE tamiza.assistant_calls (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    user_id uuid NOT NULL,
    kind character varying(16) NOT NULL,
    name character varying(63),
    action character varying(16) NOT NULL,
    -- No foreign key: script tests are deleted after 24 hours and the record outlives them.
    test_id uuid,
    model character varying(200) NOT NULL,
    prompt_kit integer NOT NULL,
    context jsonb NOT NULL,
    status character varying(16) NOT NULL,
    error_code character varying(64),
    prompt_tokens integer,
    completion_tokens integer,
    total_tokens integer,
    tokens_estimated boolean NOT NULL DEFAULT false,
    outcome character varying(16),
    started_at timestamp with time zone NOT NULL DEFAULT now(),
    finished_at timestamp with time zone,
    CONSTRAINT pk_assistant_calls PRIMARY KEY (id),
    CONSTRAINT fk_assistant_calls_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_assistant_calls_users_user_id FOREIGN KEY (user_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_assistant_calls_kind CHECK (kind IN ('clean', 'output', 'indicator')),
    CONSTRAINT ck_assistant_calls_action CHECK (action IN ('script', 'cell', 'fix')),
    CONSTRAINT ck_assistant_calls_status CHECK (status IN ('running', 'succeeded', 'failed', 'cancelled')),
    CONSTRAINT ck_assistant_calls_outcome CHECK (outcome IS NULL OR outcome IN ('script', 'cell', 'insert'))
);

-- A user has at most one request in progress.
CREATE UNIQUE INDEX ix_assistant_calls_user_id_running ON tamiza.assistant_calls (user_id) WHERE status = 'running';

-- The daily budget sums a user's rows since the start of the UTC day.
CREATE INDEX ix_assistant_calls_user_id_started_at ON tamiza.assistant_calls (user_id, started_at);

CREATE INDEX ix_assistant_calls_project_id ON tamiza.assistant_calls (project_id);
