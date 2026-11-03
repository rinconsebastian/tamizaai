import cases from '../testing/export-file-names.json';
import { WidgetFileExtension, slug, widgetFileName } from './file-name';

// The same cases the API's WidgetExportFileNameTests read (design D16 of add-widget-export): the names are worked out by hand, so the
// two implementations are checked against the same truth.

describe('widgetFileName', () => {
  it('reads the shared cases', () => {
    expect(cases.cases.length).toBe(9);
  });

  it.each(cases.cases.map((c) => [c.name, c] as const))('gives the name that was worked out by hand: %s', (_name, c) => {
    expect(
      widgetFileName({
        project: c.project,
        dashboard: c.dashboard,
        title: c.title,
        version: c.version,
        day: c.day,
        extension: c.extension as WidgetFileExtension,
      }),
    ).toBe(c.expected);
  });

  it('writes only ASCII, with no path separator', () => {
    const name = widgetFileName({ project: 'Año 2026', dashboard: 'Vivienda', title: 'Población / 住房 ‮', version: 3, day: '2026-11-02', extension: 'png' });

    expect(name).toMatch(/^[\x20-\x7e]+$/);
    expect(name).not.toContain('/');
  });
});

describe('slug', () => {
  it('cuts to 40 characters without leaving a dash at the end', () => {
    expect(slug('a'.repeat(39) + ' b', 'x')).toBe('a'.repeat(39));
  });

  it('gives the fallback when no letter or digit is left', () => {
    expect(slug('住房 🎉', 'widget')).toBe('widget');
  });
});
