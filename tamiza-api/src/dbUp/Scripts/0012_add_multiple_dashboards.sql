-- Multiple dashboards: a project's own indicator dashboards, each with its widgets. Every existing project gets one, Indicators,
-- holding all its widgets.

CREATE TABLE tamiza.dashboards (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL,
    name text NOT NULL,
    normalized_name text NOT NULL,
    position integer NOT NULL,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_dashboards PRIMARY KEY (id),
    CONSTRAINT ck_dashboards_position CHECK (position >= 0),
    CONSTRAINT fk_dashboards_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboards_users_created_by FOREIGN KEY (created_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_dashboards_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

-- One name per project whatever its case; also serves the lookups by project.
CREATE UNIQUE INDEX ix_dashboards_project_id_normalized_name ON tamiza.dashboards (project_id, normalized_name);

CREATE INDEX ix_dashboards_created_by ON tamiza.dashboards (created_by);

CREATE INDEX ix_dashboards_updated_by ON tamiza.dashboards (updated_by);

INSERT INTO tamiza.dashboards (project_id, name, normalized_name, position, created_by, updated_by)
SELECT id, 'Indicators', 'indicators', 0, created_by, created_by FROM tamiza.projects;

ALTER TABLE tamiza.dashboard_widgets ADD COLUMN dashboard_id uuid;

UPDATE tamiza.dashboard_widgets AS w SET dashboard_id = d.id FROM tamiza.dashboards AS d WHERE d.project_id = w.project_id;

ALTER TABLE tamiza.dashboard_widgets
    ALTER COLUMN dashboard_id SET NOT NULL,
    ADD CONSTRAINT fk_dashboard_widgets_dashboards_dashboard_id FOREIGN KEY (dashboard_id) REFERENCES tamiza.dashboards (id) ON DELETE CASCADE;

CREATE INDEX ix_dashboard_widgets_dashboard_id ON tamiza.dashboard_widgets (dashboard_id);
