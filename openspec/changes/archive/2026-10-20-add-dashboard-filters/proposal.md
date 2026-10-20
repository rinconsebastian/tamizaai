# Proposal

## Why

Each widget of a dashboard has its own filters, fixed by the analyst who built it. A member who wants to see one dashboard for one municipality, one round or one period has no way to do it. Today the analyst has to duplicate the dashboard and edit every widget's filters, and keep the copies in step afterwards. Analysts asked for filters on the dashboard itself: controls that the analyst defines once and that every member, viewers included, can change to narrow every widget at once. This is how the Power BI and Excel reports that Tamiza replaces work.

The multiple dashboards change (archived `2026-10-18-add-multiple-dashboards`) listed "A dashboard-wide filter, or filters shared between widgets" under "Not in this change". This change lifts that.

## What Changes

- **Dashboard filters.** An `analyst` or `admin` defines up to 8 filters on each dashboard. Each filter has:
  - a label;
  - a column name;
  - the kind of value it takes: text, true/false, number or date;
  - an optional default.

  Text and true/false filters choose values ("is one of"). Number and date filters take a range (from, to, or both), with dates in days in the project's time zone, as in the clean data dashboard. A dashboard has at most one filter per column.
- **A filter bar for every member.** The filters appear above the widgets, after the dashboard header. Any member, viewers included, can change their values, clear them or go back to the defaults. Their choice changes only their own screen and is never saved, like the map basemap switch. It is kept in the page address, so a link opens the same filtered view for anyone, and a filter that is no longer defined is dropped from such a link with a notice.
- **Which widgets a filter narrows.** A filter applies to a widget when the published table of the widget's indicator has a column with the filter's name and a value of the filter's kind. Integer and decimal columns both count as number, and date and timestamp columns both count as date.
  - When the widget also has its own filter on that column, a row must match both.
  - A widget whose table has no such column is shown unfiltered by it, and the widget says which active filters do not narrow it.
- **A widget can ignore chosen dashboard filters.** The widget form lists the dashboard's filters with a checkbox each, so a national total, for example, stays unchanged when a member filters by municipality. A widget that ignores an active filter says so the same way.
- **Choosing values from a list.** The values offered by a text filter are the most frequent values of that column across the indicator tables of the dashboard's widgets, with a search. Today an indicator table cannot list its values. Widgets' own filters keep their typed values.
- **The widget form previews with the dashboard's filters.** The preview applies the values in force on the member's screen, minus the filters the widget ignores.
- **Duplicating a dashboard copies its filters** with its widgets. A widget moved to another dashboard keeps its list of ignored filters, which are kept by column name.
- **Scripts publish the columns to filter on.** A filter can only narrow a table that has its column. So an indicator script should group by every column the dashboard filters on and publish measures that can be summed, with rates as `numerator` and `denominator`. The indicator templates gain a list of group columns, and the indicators guide gains a section on preparing indicators for dashboard filters.
- **Migration.** The DbUp script `0014_add_dashboard_filters.sql` gives every dashboard an empty list of filters. Nothing changes for existing widgets.

Not in this change:
- Exports filtered by the dashboard's filters. The indicator table export stays the whole table as published.
- Cross-filtering by clicking a bar, slice or shape.
- Filters that depend on another filter (department, then municipality).
- Relative periods such as "the last 7 days".
- Mapping a filter to differently named columns in different indicators. Columns are matched by name.
- Text filters by *contains* or *is exactly*, and *is empty* filters, on the dashboard. Widgets keep all of them.
- Filters on the Performance and Clean data dashboards. Clean data has its own.
- Saving a member's own filter values between visits.

## Capabilities

### New Capabilities

- `dashboard-filters`: the filters a dashboard defines and how members use them. It covers:
  - how they are defined, by whom, and their limits;
  - the filter bar;
  - values chosen from a list;
  - defaults, clearing, and the view kept in the address;
  - which widgets a filter narrows;
  - what a widget says about filters that do not narrow it.

### Modified Capabilities

- `dashboard-indicators`:
  - "Widget query over one indicator": the dashboard's filters are combined with the widget's own.
  - "Stale widget configuration": a dashboard filter whose column the table lacks is not a stale configuration.
  - "Moving a widget to another dashboard": the widget keeps the filters it ignores.
  - A new requirement for ignoring chosen dashboard filters, and one for the widget form's preview.
- `custom-dashboards`:
  - "Duplicating a dashboard": the copy also holds the original's filters.
  - "Roles for dashboards": editing a dashboard's filters is an `analyst` or `admin` action.

## Impact

- **api** (`tamiza-api`):
  - `PUT /api/v1/projects/{projectId}/dashboards/{dashboardId}/filters` replaces a dashboard's filters. `GET .../dashboards/{dashboardId}` returns them.
  - `GET .../dashboards/{dashboardId}/values` lists a text column's most frequent values across the dashboard's indicator tables.
  - `POST .../indicators/query` takes the dashboard filters in force and answers which ones did not apply.
  - The widget config gains its ignored dashboard filters. Duplicating a dashboard copies its filters.
  - `DataQueryBuilder` builds a second set of filters on the same columns without clashing parameters.
  - OpenAPI, unit and integration tests.
- **ui** (`tamiza-ui`):
  - `project-indicators.ts`: the filter bar, the view in the address and its notice, and the "Filters" dashboard action.
  - A new dialog to define the filters. `data-filter-dialog.ts` is reused for the values.
  - `widget-view.ts` and `widget-grid.ts`: the dashboard filters in each query and the "not filtered by" note.
  - `widget-dialog.ts`: the ignored filters and the filtered preview.
  - `indicators.service.ts`: the new endpoints and types.
  - `en.json` and `es.json` strings, and the specs.
- **Database:** `0014_add_dashboard_filters.sql` adds `dashboards.filters` (`jsonb`, an empty list by default). `TamizaDbContext` mirrors it.
- **Analytics:** none in the worker. The indicator templates in `tamiza-ui/src/app/pages/projects/script-templates/indicator/` and their copies in `tamiza-api/src/Tamiza.Api/Assistant/Examples/indicator/` change together, so `AssistantPrompts.Version` is bumped. `tests/integration/test_templates.py` still runs them unchanged.
- **Docs:**
  - `docs/indicators-dashboard.md`: dashboard filters and preparing indicators for them.
  - `docs/scripts.md`: a pointer to that section.
  - `docs/tamiza-brief.md`: the data model and the roadmap.
