-- Indicators dashboard: the widgets members arrange on a project's indicators dashboard. One row per widget; its query
-- (config) and its position and size on the 12-column grid (layout) are opaque jsonb that the API validates.

CREATE TABLE tamiza.dashboard_widgets (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    project_id uuid NOT NULL,
    chart_type text NOT NULL,
    title text NOT NULL,
    config jsonb NOT NULL DEFAULT '{}'::jsonb,
    layout jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL DEFAULT now(),
    CONSTRAINT pk_dashboard_widgets PRIMARY KEY (id),
    CONSTRAINT ck_dashboard_widgets_chart_type CHECK (chart_type IN ('stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map')),
    CONSTRAINT fk_dashboard_widgets_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_dashboard_widgets_users_created_by FOREIGN KEY (created_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_dashboard_widgets_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

-- The dashboard lists a project's widgets.
CREATE INDEX ix_dashboard_widgets_project_id ON tamiza.dashboard_widgets (project_id);

CREATE INDEX ix_dashboard_widgets_created_by ON tamiza.dashboard_widgets (created_by);

CREATE INDEX ix_dashboard_widgets_updated_by ON tamiza.dashboard_widgets (updated_by);
