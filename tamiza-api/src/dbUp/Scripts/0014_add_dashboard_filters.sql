-- Dashboard filters: each dashboard's list of filters, in order, as JSON the API validates. Existing dashboards get none.

ALTER TABLE tamiza.dashboards ADD COLUMN filters jsonb NOT NULL DEFAULT '[]'::jsonb;
