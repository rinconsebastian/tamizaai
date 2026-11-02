-- Box plot widget: a widget can be a box plot. The check on the kind is dropped and added again with the twelfth kind. A box plot's
-- orientation lives in the widget's config (display.orientation); existing widgets have none and do not change.

ALTER TABLE tamiza.dashboard_widgets DROP CONSTRAINT ck_dashboard_widgets_chart_type;
ALTER TABLE tamiza.dashboard_widgets ADD CONSTRAINT ck_dashboard_widgets_chart_type
    CHECK (chart_type IN ('stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map', 'heatmap', 'pyramid', 'likert', 'crosstab', 'boxplot'));
