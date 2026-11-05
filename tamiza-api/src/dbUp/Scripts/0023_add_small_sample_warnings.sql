-- Small-sample warnings: a dashboard can carry a small-base threshold, a whole number from 1 to 10,000 below which a mark's base n counts as
-- small, and a switch that shows each widget's base n under it. The threshold is null for every existing dashboard, which reads as off, and
-- the switch is false, so nothing changes until someone turns a setting on. The named constraint keeps a stored threshold within the API's
-- range. The columns rewrite no row.

ALTER TABLE tamiza.dashboards
    ADD COLUMN min_n integer,
    ADD COLUMN show_n boolean NOT NULL DEFAULT false,
    ADD CONSTRAINT ck_dashboards_min_n CHECK (min_n IS NULL OR min_n BETWEEN 1 AND 10000);
