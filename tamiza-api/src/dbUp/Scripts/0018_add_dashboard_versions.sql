-- Dashboard versions. Every change saved to a dashboard records the dashboard as it is after the change: its name, its filters and its
-- widgets (content), with what changed (changes). A dashboard keeps its 10 newest versions; older ones are deleted when a new one is
-- recorded, and all of them go with the dashboard. Origin says whether the change was made in Tamiza or by an AI client, and note holds
-- the request the AI client gave. created_at is the first write of a version and saved_at its last, since one request of an AI
-- client makes one version. Version 1 of every existing dashboard holds its state when version history began.

CREATE TABLE tamiza.dashboard_versions (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    dashboard_id uuid NOT NULL,
    number integer NOT NULL,
    content jsonb NOT NULL,
    changes jsonb NOT NULL DEFAULT '[]',
    more_changes integer NOT NULL DEFAULT 0,
    restored_from integer,
    origin character varying(8) NOT NULL DEFAULT 'app',
    note character varying(120),
    author_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    saved_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_dashboard_versions PRIMARY KEY (id),
    CONSTRAINT fk_dashboard_versions_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_versions_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES tamiza.dashboards (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_versions_users_author_id FOREIGN KEY (author_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT ck_dashboard_versions_number CHECK (number >= 1),
    CONSTRAINT ck_dashboard_versions_more_changes CHECK (more_changes >= 0),
    CONSTRAINT ck_dashboard_versions_origin CHECK (origin IN ('app', 'mcp')),
    CONSTRAINT ck_dashboard_versions_note CHECK (note IS NULL OR origin = 'mcp')
);

CREATE UNIQUE INDEX ix_dashboard_versions_dashboard_id_number ON tamiza.dashboard_versions (dashboard_id, number);
CREATE INDEX ix_dashboard_versions_project_id ON tamiza.dashboard_versions (project_id);
CREATE INDEX ix_dashboard_versions_author_id ON tamiza.dashboard_versions (author_id);

INSERT INTO tamiza.dashboard_versions (id, project_id, dashboard_id, number, content, changes, origin, author_id, created_at, saved_at)
SELECT gen_random_uuid(), d.project_id, d.id, 1,
       jsonb_build_object(
           'name', d.name,
           'filters', d.filters,
           'widgets', COALESCE((
               SELECT jsonb_agg(jsonb_build_object(
                          'id', w.id, 'title', w.title, 'chartType', w.chart_type, 'config', w.config, 'layout', w.layout)
                      ORDER BY (w.layout ->> 'y')::integer, (w.layout ->> 'x')::integer, w.created_at, w.id)
               FROM tamiza.dashboard_widgets w
               WHERE w.dashboard_id = d.id), '[]'::jsonb)),
       '[{"kind": "initial"}]'::jsonb,
       'app',
       last_change.updated_by, last_change.updated_at, last_change.updated_at
FROM tamiza.dashboards d
CROSS JOIN LATERAL (
    SELECT edits.updated_by, edits.updated_at
    FROM (
        SELECT d.updated_by, d.updated_at
        UNION ALL
        SELECT w.updated_by, w.updated_at FROM tamiza.dashboard_widgets w WHERE w.dashboard_id = d.id
    ) AS edits
    ORDER BY edits.updated_at DESC
    LIMIT 1
) AS last_change;
