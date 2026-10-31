-- Two-dimension widgets: a widget can be a heat map, a population pyramid, diverging Likert bars or a cross table. The check on the kind
-- is dropped and added again with the four new kinds. A widget's series settings live in its config; existing widgets have none.

ALTER TABLE tamiza.dashboard_widgets DROP CONSTRAINT ck_dashboard_widgets_chart_type;
ALTER TABLE tamiza.dashboard_widgets ADD CONSTRAINT ck_dashboard_widgets_chart_type
    CHECK (chart_type IN ('stat', 'bar', 'column', 'line', 'histogram', 'pie', 'map', 'heatmap', 'pyramid', 'likert', 'crosstab'));
