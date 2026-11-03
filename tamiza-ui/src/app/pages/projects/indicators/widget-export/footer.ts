/** What the footer of a widget's picture says (add-widget-export, design D8); the exporter has written each piece in the member's language. */
export interface FooterFacts {
  project: string;
  dashboard: string;
  /** "Version 9, saved by Ana Pérez on …" while a version is shown. */
  version: string | null;
  /** Every filter of the dashboard as the filter bar writes it, "Municipality: Cali", with "All" for one without a value. */
  filters: string[];
  /** "Data published Nov 2, 2026, 5:12 AM", or the words for a table not published yet. */
  published: string;
  /** "Exported Nov 2, 2026, 10:30 AM (America/Bogota)". */
  exported: string;
}

/**
 * The footer's lines: where the widget comes from, the filters in force (none when the dashboard has no filters), and when the data
 * was published and the picture made. Carries no logo and no mark of Tamiza.
 */
export function footerLines(facts: FooterFacts): string[] {
  return [
    [facts.project, facts.dashboard, facts.version].filter((part): part is string => !!part).join(' · '),
    ...(facts.filters.length ? [facts.filters.join(' · ')] : []),
    `${facts.published} · ${facts.exported}`,
  ];
}
