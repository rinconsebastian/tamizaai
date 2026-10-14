-- Indicator scripts: script 3 (one table of five fixed columns) becomes any number of named indicator scripts, each
-- publishing its own table, with a label, a description, a unit and a format saved on every version.
--
-- This is a clean cut, before real data: it DELETES every version, test and run step of script 3, every dashboard widget
-- (its config names a column that no longer exists) and every p_<id>_indicators schema.

-- Runs no longer point at one script 3 version; they snapshot the version of each indicator they used.
DROP INDEX tamiza.ix_runs_indicators_version_id;

ALTER TABLE tamiza.runs
    DROP CONSTRAINT fk_runs_script_versions_indicators_version_id,
    DROP COLUMN indicators_version_id,
    ADD COLUMN indicator_versions jsonb NOT NULL DEFAULT '[]'::jsonb;

DELETE FROM tamiza.run_steps WHERE step = 'indicators';

ALTER TABLE tamiza.run_steps
    DROP CONSTRAINT ck_run_steps_step,
    DROP CONSTRAINT pk_run_steps,
    ADD COLUMN name character varying(63) NOT NULL DEFAULT '',
    ADD CONSTRAINT pk_run_steps PRIMARY KEY (run_id, step, name),
    ADD CONSTRAINT ck_run_steps_step CHECK (step IN ('flatten', 'clean', 'output', 'indicator')),
    ADD CONSTRAINT ck_run_steps_name CHECK ((step = 'indicator') = (name <> ''));

DELETE FROM tamiza.script_tests WHERE kind = 'indicators';

ALTER TABLE tamiza.script_tests
    DROP CONSTRAINT ck_script_tests_kind,
    ADD COLUMN name character varying(63),
    ADD CONSTRAINT ck_script_tests_kind CHECK (kind IN ('clean', 'output', 'indicator')),
    ADD CONSTRAINT ck_script_tests_name CHECK ((kind = 'indicator') = (name IS NOT NULL));

DELETE FROM tamiza.script_versions WHERE kind = 'indicators';

DROP INDEX tamiza.ix_script_versions_project_id_kind_number;

ALTER TABLE tamiza.script_versions
    DROP CONSTRAINT ck_script_versions_kind,
    ADD COLUMN name character varying(63),
    ADD COLUMN label text,
    ADD COLUMN description text,
    ADD COLUMN unit character varying(32),
    ADD COLUMN format character varying(16),
    ADD CONSTRAINT ck_script_versions_kind CHECK (kind IN ('clean', 'output', 'indicator')),
    ADD CONSTRAINT ck_script_versions_name CHECK ((kind = 'indicator') = (name IS NOT NULL)),
    ADD CONSTRAINT ck_script_versions_format CHECK (format IS NULL OR format IN ('count', 'decimal', 'percent', 'currency'));

-- Versions are numbered per indicator; the clean and output scripts have no name, and their versions still collide.
CREATE UNIQUE INDEX ix_script_versions_project_id_kind_name_number ON tamiza.script_versions (project_id, kind, name, number) NULLS NOT DISTINCT;

DELETE FROM tamiza.dashboard_widgets;

DO $$
DECLARE
    indicator_schema text;
BEGIN
    FOR indicator_schema IN SELECT nspname FROM pg_namespace WHERE nspname ~ '^p_[0-9a-f]{32}_indicators$' LOOP
        EXECUTE format('DROP SCHEMA %I CASCADE', indicator_schema);
    END LOOP;
END
$$;
