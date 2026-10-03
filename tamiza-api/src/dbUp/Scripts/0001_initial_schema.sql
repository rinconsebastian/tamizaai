-- Metadata schema as left by the EF Core migrations up to 20261003010233_AddProjects.
-- Databases created by those migrations are adopted without running this script (see SchemaMigrator).

CREATE SCHEMA IF NOT EXISTS tamiza;

CREATE TABLE tamiza.users (
    id uuid NOT NULL,
    keycloak_sub character varying(255) NOT NULL,
    name character varying(255) NOT NULL,
    email character varying(320),
    email_verified boolean NOT NULL DEFAULT FALSE,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id)
);

CREATE UNIQUE INDEX ix_users_keycloak_sub ON tamiza.users (keycloak_sub);

CREATE TABLE tamiza.projects (
    id uuid NOT NULL,
    name character varying(200) NOT NULL,
    kobo_server_url character varying(2048) NOT NULL,
    kobo_asset_uid character varying(64) NOT NULL,
    encrypted_api_token text NOT NULL,
    encrypted_webhook_secret text NOT NULL,
    form_name character varying(500) NOT NULL,
    form_fields jsonb NOT NULL,
    form_checked_at timestamp with time zone NOT NULL,
    enumerator_field character varying(1024),
    created_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_projects PRIMARY KEY (id),
    CONSTRAINT fk_projects_users_created_by FOREIGN KEY (created_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_projects_created_by ON tamiza.projects (created_by);

CREATE TABLE tamiza.project_invitations (
    id uuid NOT NULL,
    project_id uuid NOT NULL,
    email character varying(320) NOT NULL,
    normalized_email character varying(320) NOT NULL,
    role character varying(16) NOT NULL,
    invited_by uuid NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_project_invitations PRIMARY KEY (id),
    CONSTRAINT ck_project_invitations_role CHECK (role IN ('admin', 'analyst', 'viewer')),
    CONSTRAINT fk_project_invitations_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_project_invitations_users_invited_by FOREIGN KEY (invited_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_project_invitations_invited_by ON tamiza.project_invitations (invited_by);

CREATE INDEX ix_project_invitations_normalized_email ON tamiza.project_invitations (normalized_email);

CREATE UNIQUE INDEX ix_project_invitations_project_id_normalized_email ON tamiza.project_invitations (project_id, normalized_email);

CREATE TABLE tamiza.project_members (
    project_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role character varying(16) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_project_members PRIMARY KEY (project_id, user_id),
    CONSTRAINT ck_project_members_role CHECK (role IN ('admin', 'analyst', 'viewer')),
    CONSTRAINT fk_project_members_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_project_members_users_user_id FOREIGN KEY (user_id) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_project_members_user_id ON tamiza.project_members (user_id);

CREATE TABLE tamiza.sampling_frames (
    project_id uuid NOT NULL,
    dimensions jsonb NOT NULL,
    targets jsonb NOT NULL,
    updated_by uuid NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_sampling_frames PRIMARY KEY (project_id),
    CONSTRAINT fk_sampling_frames_projects_project_id FOREIGN KEY (project_id) REFERENCES tamiza.projects (id) ON DELETE CASCADE,
    CONSTRAINT fk_sampling_frames_users_updated_by FOREIGN KEY (updated_by) REFERENCES tamiza.users (id) ON DELETE RESTRICT
);

CREATE INDEX ix_sampling_frames_updated_by ON tamiza.sampling_frames (updated_by);
