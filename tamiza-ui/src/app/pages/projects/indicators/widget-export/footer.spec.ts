import { footerLines } from './footer';

describe('footerLines', () => {
  const facts = {
    project: 'Census 2026',
    dashboard: 'Housing',
    version: null,
    source: null,
    filters: ['Municipality: Cali', 'Round: All'],
    published: 'Data published Nov 2, 2026, 5:12 AM',
    exported: 'Exported Nov 2, 2026, 10:30 AM (America/Bogota)',
  };

  it('names the project and the dashboard, the filters in force, and when the data was published and the picture made', () => {
    expect(footerLines(facts)).toEqual([
      'Census 2026 · Housing',
      'Municipality: Cali · Round: All',
      'Data published Nov 2, 2026, 5:12 AM · Exported Nov 2, 2026, 10:30 AM (America/Bogota)',
    ]);
  });

  it('names the version while one is shown', () => {
    expect(footerLines({ ...facts, version: 'Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM' })[0]).toBe(
      'Census 2026 · Housing · Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM',
    );
  });

  it('gives the source of the dashboard a line after the project, dashboard and version (Source in the footer)', () => {
    expect(footerLines({ ...facts, source: 'Source: Encuesta de mercado de la zona, 478 encuestas' })).toEqual([
      'Census 2026 · Housing',
      'Source: Encuesta de mercado de la zona, 478 encuestas',
      'Municipality: Cali · Round: All',
      'Data published Nov 2, 2026, 5:12 AM · Exported Nov 2, 2026, 10:30 AM (America/Bogota)',
    ]);
  });

  it('keeps the source after the version line while one is shown', () => {
    const lines = footerLines({ ...facts, version: 'Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM', source: 'Source: Encuesta de la zona' });

    expect(lines.slice(0, 2)).toEqual(['Census 2026 · Housing · Version 9, saved by Ana Pérez on Oct 31, 2026, 10:32 AM', 'Source: Encuesta de la zona']);
  });

  it('has no line of a source for a dashboard without one (Dashboard without a source)', () => {
    expect(footerLines(facts)).toHaveLength(3);
    expect(footerLines({ ...facts, source: '' })).toHaveLength(3);
  });

  it('has no line of filters for a dashboard without filters', () => {
    expect(footerLines({ ...facts, filters: [] })).toHaveLength(2);
  });
});
