-- Dashboard palettes: each dashboard has one palette, chosen from a fixed set the check below lists, and a list of colors kept for values
-- of its columns, as JSON the API validates (column, value and the palette color's number). Existing dashboards read as the palette
-- tamiza with no colors kept, so they look as they did. The columns rewrite no row.

ALTER TABLE tamiza.dashboards
    ADD COLUMN palette text NOT NULL DEFAULT 'tamiza',
    ADD COLUMN value_colors jsonb NOT NULL DEFAULT '[]',
    ADD CONSTRAINT ck_dashboards_palette CHECK (palette IN ('tamiza', 'institutional', 'earth', 'vivid'));
