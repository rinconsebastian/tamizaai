-- Widget context: a dashboard can carry a source, a short free text of 1 to 300 characters that says where its data comes from, such as the
-- survey. The column is null for every existing dashboard, which reads as having no source, and the named constraint below keeps a stored
-- source within the API's length (char_length counts code points, at most as many as the API's count of UTF-16 units). It rewrites no row.

ALTER TABLE tamiza.dashboards
    ADD COLUMN source text NULL,
    ADD CONSTRAINT ck_dashboards_source CHECK (source IS NULL OR char_length(source) BETWEEN 1 AND 300);
