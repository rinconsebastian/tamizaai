import { footerLines } from './footer';

describe('footerLines', () => {
  const facts = {
    project: 'Census 2026',
    dashboard: 'Housing',
    version: null,
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

  it('has no line of filters for a dashboard without filters', () => {
    expect(footerLines({ ...facts, filters: [] })).toHaveLength(2);
  });
});
