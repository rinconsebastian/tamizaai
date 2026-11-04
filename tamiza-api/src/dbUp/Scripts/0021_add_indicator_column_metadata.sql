-- Indicator column metadata: an indicator version can hold facts about the columns of its table, as one JSON object from a column's
-- name to its properties (today only the label shown for the column). The column is null for script 1 and script 2, for a removal and
-- for an indicator version that describes no column, so existing versions read as describing none. It rewrites no row.

ALTER TABLE tamiza.script_versions
    ADD COLUMN columns jsonb,
    ADD CONSTRAINT ck_script_versions_columns
        CHECK (columns IS NULL OR (kind = 'indicator' AND jsonb_typeof(columns) = 'object'));
