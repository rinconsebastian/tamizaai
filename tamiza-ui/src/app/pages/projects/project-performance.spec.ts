import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { RouterTestingHarness } from '@angular/router/testing';
import { PerformanceDashboard } from '../../core/projects/performance.service';
import { ProjectDetails, ProjectRole } from '../../core/projects/projects.service';
import { byTestId, openProjectSection, projectId, settle } from '../../../testing/project-harness';

const url = `/api/v1/projects/${projectId}/performance`;

function dashboard(overrides: Partial<PerformanceDashboard> = {}): PerformanceDashboard {
  return {
    computedAt: '2026-10-02T15:42:00Z',
    timeZone: 'UTC',
    from: null,
    to: null,
    availability: [],
    submissions: {
      total: 14,
      withoutReceptionTime: 2,
      days: [
        { day: '2026-09-01', count: 10 },
        { day: '2026-09-02', count: 0 },
        { day: '2026-09-03', count: 4 },
      ],
    },
    completionTime: {
      available: true,
      missingFields: [],
      usableDurations: 10,
      unusableDurations: 4,
      outliers: 1,
      medianSeconds: 1440,
      meanWithoutOutliersSeconds: 1500,
      lowerFenceSeconds: 300,
      upperFenceSeconds: 3000,
    },
    enumerators: {
      available: true,
      hint: null,
      rows: [
        { name: 'Luis', submissions: 5, meanSeconds: 1300, outliers: 0, lastSubmissionAt: '2026-09-03T18:00:00Z' },
        { name: 'Ana', submissions: 8, meanSeconds: 3900, outliers: 1, lastSubmissionAt: '2026-09-03T20:00:00Z' },
        { name: null, submissions: 1, meanSeconds: null, outliers: 0, lastSubmissionAt: null },
      ],
    },
    coverage: {
      state: 'configured',
      dimensions: [
        { name: 'Municipality', field: 'municipality', warning: null },
        { name: 'Sex', field: 'sex', warning: null },
      ],
      rows: [
        { values: [{ code: 'cali', label: 'Cali' }, { code: 'f', label: 'Female' }], target: 120, achieved: 90, percent: 75 },
        { values: [{ code: 'cali', label: 'Cali' }, { code: 'm', label: null }], target: 100, achieved: 130, percent: 130 },
        { values: [{ code: 'bogota', label: 'Bogotá' }, { code: 'f', label: 'Female' }], target: 0, achieved: 5, percent: null },
      ],
      overallPercent: 75,
      outsideFrame: 40,
      outsideTop: [{ values: [{ code: '05001', label: null }, { code: 'f', label: 'Female' }], submissions: 40 }],
    },
    ...overrides,
  };
}

function isPerformance(request: { url: string }): boolean {
  return request.url === url;
}

async function open(role: ProjectRole, data: PerformanceDashboard = dashboard(), overrides: Partial<ProjectDetails> = {}) {
  const context = await openProjectSection('performance', role, overrides);
  context.http.expectOne(isPerformance).flush(data);
  await settle(context.harness);
  return context;
}

function setDate(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('change', { bubbles: true }));
}

async function pressRefresh(root: HTMLElement, harness: RouterTestingHarness, http: HttpTestingController): Promise<TestRequest> {
  byTestId<HTMLButtonElement>(root, 'refresh')!.click();
  await settle(harness);
  return http.expectOne(isPerformance);
}

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('ProjectPerformance', () => {
  describe('loading', () => {
    it.each<ProjectRole>(['viewer', 'analyst', 'admin'])('asks for the whole project on open, for a %s', async (role) => {
      const { http, harness, root } = await openProjectSection('performance', role);
      const request = http.expectOne(isPerformance);
      expect(request.request.method).toBe('GET');
      expect(request.request.params.keys()).toEqual([]);

      expect(byTestId(root, 'performance-loading')).not.toBeNull();
      request.flush(dashboard());
      await settle(harness);
      expect(byTestId(root, 'performance-loading')).toBeNull();
      expect(byTestId(root, 'submissions-card')).not.toBeNull();
    });

    it('shows when the figures were computed', async () => {
      const { root } = await open('viewer');
      expect(text(byTestId(root, 'computed-at'))).toMatch(/^Computed at .*2026/);
    });

    it('shows the timeout message and no figures, and Refresh tries again', async () => {
      const { http, harness, root } = await openProjectSection('performance', 'analyst');
      http.expectOne(isPerformance).flush({ code: 'dashboard.timeout', title: 'Timeout' }, { status: 503, statusText: 'Service Unavailable' });
      await settle(harness);

      expect(text(byTestId(root, 'performance-error'))).toContain('took too long');
      expect(byTestId(root, 'submissions-card')).toBeNull();

      (await pressRefresh(root, harness, http)).flush(dashboard());
      await settle(harness);
      expect(byTestId(root, 'performance-error')).toBeNull();
      expect(byTestId(root, 'submissions-card')).not.toBeNull();
    });

    it('keeps the old figures while Refresh is computing', async () => {
      const { http, harness, root } = await open('viewer');
      byTestId<HTMLButtonElement>(root, 'refresh')!.click();
      await settle(harness);

      expect(byTestId(root, 'submissions-card')).not.toBeNull();
      expect(byTestId<HTMLButtonElement>(root, 'refresh')!.disabled).toBe(true);
      http.expectOne(isPerformance).flush(dashboard());
    });
  });

  describe('range', () => {
    it('sends both days as from and to and reloads', async () => {
      const { http, harness, root } = await open('viewer');
      setDate(byTestId<HTMLInputElement>(root, 'range-from')!, '2026-09-01');
      await settle(harness);
      const first = http.expectOne(isPerformance);
      expect(first.request.params.get('from')).toBe('2026-09-01');
      expect(first.request.params.has('to')).toBe(false);
      first.flush(dashboard({ from: '2026-09-01' }));
      await settle(harness);

      setDate(byTestId<HTMLInputElement>(root, 'range-to')!, '2026-09-07');
      await settle(harness);
      const second = http.expectOne(isPerformance);
      expect(second.request.params.get('from')).toBe('2026-09-01');
      expect(second.request.params.get('to')).toBe('2026-09-07');
      second.flush(dashboard({ from: '2026-09-01', to: '2026-09-07' }));
    });

    it('sends only the last day when that is the one bound', async () => {
      const { http, harness, root } = await open('viewer');
      setDate(byTestId<HTMLInputElement>(root, 'range-to')!, '2026-09-07');
      await settle(harness);

      const request = http.expectOne(isPerformance);
      expect(request.request.params.has('from')).toBe(false);
      expect(request.request.params.get('to')).toBe('2026-09-07');
      request.flush(dashboard());
    });

    it('clears the range and asks for the whole project', async () => {
      const { http, harness, root } = await open('viewer');
      expect(byTestId<HTMLButtonElement>(root, 'range-clear')!.disabled).toBe(true);
      setDate(byTestId<HTMLInputElement>(root, 'range-from')!, '2026-09-01');
      await settle(harness);
      http.expectOne(isPerformance).flush(dashboard());
      await settle(harness);

      expect(byTestId<HTMLButtonElement>(root, 'range-clear')!.disabled).toBe(false);
      byTestId<HTMLButtonElement>(root, 'range-clear')!.click();
      await settle(harness);

      const request = http.expectOne(isPerformance);
      expect(request.request.params.keys()).toEqual([]);
      expect(byTestId<HTMLInputElement>(root, 'range-from')!.value).toBe('');
      request.flush(dashboard());
    });

    it('shows the API message for a reversed range next to the inputs', async () => {
      const { http, harness, root } = await open('viewer');
      setDate(byTestId<HTMLInputElement>(root, 'range-from')!, '2026-09-07');
      await settle(harness);
      http.expectOne(isPerformance).flush(
        { errors: { from: ['The first day is after the last day.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle(harness);

      expect(text(root.querySelector('#performanceFrom-error'))).toBe('The first day is after the last day.');
      expect(byTestId(root, 'performance-error')).toBeNull();
    });
  });

  describe('refresh', () => {
    it('asks again and shows the new computation time', async () => {
      const { http, harness, root } = await open('analyst');
      const before = text(byTestId(root, 'computed-at'));

      const request = await pressRefresh(root, harness, http);
      request.flush(dashboard({ computedAt: '2026-10-02T15:50:00Z' }));
      await settle(harness);

      expect(text(byTestId(root, 'computed-at'))).not.toBe(before);
      http.verify();
    });
  });

  describe('days notice', () => {
    it('says days are in UTC for the default zone, and offers admins the Overview', async () => {
      const { root } = await open('admin');
      expect(text(byTestId(root, 'days-zone'))).toContain('Days are counted in UTC.');
      expect(byTestId(root, 'zone-link')?.getAttribute('href')).toBe(`/projects/${projectId}/overview`);
    });

    it('does not offer the Overview to members who cannot change the zone', async () => {
      const { root } = await open('analyst');
      expect(text(byTestId(root, 'days-zone'))).toContain('Days are counted in UTC.');
      expect(byTestId(root, 'zone-link')).toBeNull();
    });

    it('names the zone when the project has one', async () => {
      const { root } = await open('viewer', dashboard({ timeZone: 'America/Bogota' }));
      expect(text(byTestId(root, 'days-zone'))).toBe('Days are counted in the America/Bogota time zone.');
    });
  });

  describe('submissions', () => {
    it('shows the totals, a chart with a summary label, and the count without reception time', async () => {
      const { root } = await open('viewer');
      expect(text(byTestId(root, 'total'))).toBe('14');
      expect(text(byTestId(root, 'without-time'))).toBe('2');

      const chart = byTestId(root, 'days-chart')!;
      const svg = chart.querySelector('svg')!;
      expect(svg.getAttribute('role')).toBe('img');
      expect(svg.getAttribute('aria-label')).toContain('14 submissions in 3 days');
      expect(svg.getAttribute('aria-label')).toContain('at most 10 in one day');
      const bars = svg.querySelectorAll('rect');
      expect(bars).toHaveLength(3);
      expect(bars[0].querySelector('title')?.textContent).toBe('2026-09-01: 10');
      expect(Number(bars[0].getAttribute('height'))).toBeGreaterThan(Number(bars[2].getAttribute('height')));
      expect(Number(bars[1].getAttribute('height'))).toBe(0);
      expect(text(chart)).toContain('2026-09-01');
      expect(text(chart)).toContain('2026-09-03');
    });

    it('shows the same numbers as a table and goes back to the chart', async () => {
      const { harness, root } = await open('viewer');
      const toggle = byTestId<HTMLButtonElement>(root, 'chart-toggle')!;
      expect(text(toggle)).toBe('Show as table');
      expect(toggle.getAttribute('aria-pressed')).toBe('false');

      toggle.click();
      await settle(harness);
      const rows = Array.from(byTestId(root, 'days-table')!.querySelectorAll('tbody tr')).map((row) => Array.from(row.querySelectorAll('td')).map((c) => text(c)));
      expect(rows).toEqual([
        ['2026-09-01', '10'],
        ['2026-09-02', '0'],
        ['2026-09-03', '4'],
      ]);
      expect(byTestId(root, 'days-chart')).toBeNull();
      expect(text(toggle)).toBe('Show as chart');

      toggle.click();
      await settle(harness);
      expect(byTestId(root, 'days-chart')).not.toBeNull();
    });

    it('says so when there are no days', async () => {
      const { root } = await open('viewer', dashboard({ submissions: { total: 3, withoutReceptionTime: 3, days: [] } }));
      expect(byTestId(root, 'days-empty')).not.toBeNull();
      expect(byTestId(root, 'chart-toggle')).toBeNull();
      expect(text(byTestId(root, 'total'))).toBe('3');
    });

    it('formats thousands with separators', async () => {
      const { root } = await open('viewer', dashboard({ submissions: { total: 12345, withoutReceptionTime: 0, days: [] } }));
      expect(text(byTestId(root, 'total'))).toBe('12,345');
    });
  });

  describe('completion time', () => {
    it('shows the tiles and the fences in minutes', async () => {
      const { root } = await open('viewer');
      expect(text(byTestId(root, 'median'))).toBe('24 min');
      expect(text(byTestId(root, 'mean'))).toBe('25 min');
      expect(text(byTestId(root, 'outliers'))).toBe('1');
      expect(text(byTestId(root, 'unusable'))).toBe('4');
      expect(text(byTestId(root, 'fences'))).toContain('shorter than 5 min or longer than 50 min');
    });

    it.each<[number, string]>([
      [30, '30 s'],
      [59.6, '1 min'],
      [3900, '1 h 05 min'],
      [7200, '2 h 00 min'],
    ])('writes %d seconds as %s', async (seconds, expected) => {
      const base = dashboard();
      const { root } = await open('viewer', dashboard({ completionTime: { ...base.completionTime, medianSeconds: seconds } }));
      expect(text(byTestId(root, 'median'))).toBe(expected);
    });

    it('starts the fences at zero when the lower one is negative', async () => {
      const base = dashboard();
      const { root } = await open('viewer', dashboard({ completionTime: { ...base.completionTime, lowerFenceSeconds: -600 } }));
      expect(text(byTestId(root, 'fences'))).toContain('shorter than 0 min');
    });

    it('says there is nothing to compute when no duration is usable', async () => {
      const { root } = await open(
        'viewer',
        dashboard({
          completionTime: {
            available: true, missingFields: [], usableDurations: 0, unusableDurations: 3, outliers: 0,
            medianSeconds: null, meanWithoutOutliersSeconds: null, lowerFenceSeconds: null, upperFenceSeconds: null,
          },
        }),
      );
      expect(text(byTestId(root, 'median'))).toBe('—');
      expect(byTestId(root, 'no-usable')).not.toBeNull();
      expect(text(byTestId(root, 'unusable'))).toBe('3');
    });

    const unavailableTime = () =>
      dashboard({
        completionTime: {
          available: false, missingFields: ['start', 'end'], usableDurations: null, unusableDurations: null, outliers: null,
          medianSeconds: null, meanWithoutOutliersSeconds: null, lowerFenceSeconds: null, upperFenceSeconds: null,
        },
      });

    it.each<ProjectRole>(['analyst', 'admin'])('explains an unavailable metric to a %s, naming the missing field, with a link to re-check', async (role) => {
      const { root } = await open(role, unavailableTime());
      const notice = byTestId(root, 'completion-unavailable')!;
      expect(notice.getAttribute('class')).toContain('border-warning-subtle'); // a full alert for someone who can act
      expect(text(notice)).toContain('Missing in the form: start, end');
      expect(text(notice)).toContain('enable the start and end metadata');
      expect(byTestId(root, 'completion-link')?.getAttribute('href')).toBe(`/projects/${projectId}/overview`);
      expect(byTestId(root, 'median')).toBeNull();
      // The other parts still show.
      expect(byTestId(root, 'enumerator-table')).not.toBeNull();
    });

    it('tells a viewer the same, in one muted line without a link', async () => {
      const { root } = await open('viewer', unavailableTime());
      const notice = byTestId(root, 'completion-unavailable')!;
      expect(notice.tagName).toBe('P');
      expect(text(notice)).toContain('Missing in the form: start, end');
      expect(text(notice)).toContain('enable the start and end metadata');
      expect(text(notice)).toContain('an analyst or admin');
      expect(byTestId(root, 'completion-link')).toBeNull();
    });

    it('puts a dash and a short reason in the headline strip when the part is unavailable', async () => {
      const { root } = await open('viewer', unavailableTime());
      expect(text(byTestId(root, 'median-unavailable'))).toBe('—');
      expect(text(byTestId(root, 'headline'))).toContain('Not available');
    });

    it('shows the unusable count, and no mean, when no duration is usable', async () => {
      const { root } = await open(
        'viewer',
        dashboard({
          completionTime: {
            available: true, missingFields: [], usableDurations: 0, unusableDurations: 3, outliers: 0,
            medianSeconds: null, meanWithoutOutliersSeconds: null, lowerFenceSeconds: null, upperFenceSeconds: null,
          },
        }),
      );
      expect(byTestId(root, 'mean')).toBeNull();
      expect(text(byTestId(root, 'unusable'))).toBe('3');
    });
  });

  describe('enumerators', () => {
    const names = (root: HTMLElement) => Array.from(root.querySelectorAll('[data-testid="enumerator-row"]')).map((row) => text(row.querySelector('td')));

    it('lists the most active first and puts the submissions without an enumerator last', async () => {
      const { root } = await open('viewer');
      expect(names(root)).toEqual(['Ana', 'Luis', 'No enumerator']);

      const ana = Array.from(root.querySelector('[data-testid="enumerator-row"]')!.querySelectorAll('td')).map((cell) => text(cell));
      expect(ana.slice(1, 4)).toEqual(['8', '1 h 05 min', '1']);
      expect(ana[4]).toContain('2026');
      const none = Array.from(root.querySelectorAll('[data-testid="enumerator-row"]')[2].querySelectorAll('td')).map((cell) => text(cell));
      expect(none.slice(2)).toEqual(['—', '0', '—']);
    });

    it('sorts by submissions or by name, keeping the submissions without an enumerator last', async () => {
      const rows = [
        { name: 'Zoe', submissions: 9, meanSeconds: 60, outliers: 0, lastSubmissionAt: null },
        { name: 'Abel', submissions: 2, meanSeconds: 60, outliers: 0, lastSubmissionAt: null },
        { name: null, submissions: 50, meanSeconds: null, outliers: 0, lastSubmissionAt: null },
      ];
      const { harness, root } = await open('viewer', dashboard({ enumerators: { available: true, hint: null, rows } }));
      expect(names(root)).toEqual(['Zoe', 'Abel', 'No enumerator']);

      const select = byTestId<HTMLSelectElement>(root, 'enumerator-sort')!;
      select.value = 'name';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      await settle(harness);
      expect(names(root)).toEqual(['Abel', 'Zoe', 'No enumerator']);
    });

    it('also renders one card per enumerator for phones', async () => {
      const { root } = await open('viewer');
      const cards = root.querySelectorAll('[data-testid="enumerator-card"]');
      expect(cards).toHaveLength(3);
      expect(text(cards[0])).toContain('Ana');
      expect(text(cards[0])).toContain('1 h 05 min');
      expect(byTestId(root, 'enumerator-cards')!.className).toContain('md:hidden');
      expect(byTestId(root, 'enumerator-table')!.closest('tmz-table-container')!.className).toContain('hidden md:block');
    });

    it('pages at 50 rows', async () => {
      const rows = Array.from({ length: 120 }, (_, i) => ({
        name: `Enumerator ${String(i).padStart(3, '0')}`, submissions: 1000 - i, meanSeconds: 60, outliers: 0, lastSubmissionAt: null,
      }));
      const { harness, root } = await open('viewer', dashboard({ enumerators: { available: true, hint: null, rows } }));
      expect(root.querySelectorAll('[data-testid="enumerator-row"]')).toHaveLength(50);
      expect(text(byTestId(root, 'enumerator-page-info'))).toBe('1–50 of 120');
      expect(byTestId<HTMLButtonElement>(root, 'enumerator-previous')!.disabled).toBe(true);

      byTestId<HTMLButtonElement>(root, 'enumerator-next')!.click();
      await settle(harness);
      byTestId<HTMLButtonElement>(root, 'enumerator-next')!.click();
      await settle(harness);
      expect(root.querySelectorAll('[data-testid="enumerator-row"]')).toHaveLength(20);
      expect(text(byTestId(root, 'enumerator-page-info'))).toBe('101–120 of 120');
      expect(byTestId<HTMLButtonElement>(root, 'enumerator-next')!.disabled).toBe(true);
    });

    it('does not page 50 rows or fewer', async () => {
      const { root } = await open('viewer');
      expect(byTestId(root, 'enumerator-page-info')).toBeNull();
    });

    const unavailable = () =>
      dashboard({ enumerators: { available: false, hint: 'selectEnumeratorField', rows: [] } });

    it('explains an unavailable part to an admin, with a link to the Overview selector', async () => {
      const { root } = await open('admin', unavailable());
      const notice = byTestId(root, 'enumerators-unavailable')!;
      expect(text(notice)).toContain('Tamiza cannot tell who collected each submission');
      expect(byTestId(root, 'enumerators-link')?.getAttribute('href')).toBe(`/projects/${projectId}/overview#enumeratorField`);
      expect(byTestId(root, 'enumerator-table')).toBeNull();
    });

    it.each<ProjectRole>(['analyst', 'viewer'])('explains an unavailable part to a %s without a link', async (role) => {
      const { root } = await open(role, unavailable());
      const notice = byTestId(root, 'enumerators-unavailable')!;
      expect(text(notice)).toContain('Ask a project admin');
      expect(byTestId(root, 'enumerators-link')).toBeNull();
    });

    it('says so when there are no submissions', async () => {
      const { root } = await open('viewer', dashboard({ enumerators: { available: true, hint: null, rows: [] } }));
      expect(byTestId(root, 'enumerators-empty')).not.toBeNull();
    });

    it('shows a name as text, never as markup', async () => {
      const rows = [{ name: '<img src=x onerror=alert(1)><b>Mallory</b>', submissions: 1, meanSeconds: null, outliers: 0, lastSubmissionAt: null }];
      const { root } = await open('viewer', dashboard({ enumerators: { available: true, hint: null, rows } }));
      const cell = root.querySelector('[data-testid="enumerator-row"] td')!;
      expect(cell.querySelector('img, b')).toBeNull();
      expect(text(cell)).toBe('<img src=x onerror=alert(1)><b>Mallory</b>');
    });
  });

  describe('coverage without a frame', () => {
    const none = () => dashboard({ coverage: { state: 'notConfigured', dimensions: [], rows: [], overallPercent: null, outsideFrame: 0, outsideTop: [] } });

    it.each<ProjectRole>(['analyst', 'admin'])('invites a %s to define it, with a link to the sampling frame', async (role) => {
      const { root } = await open(role, none());
      expect(text(byTestId(root, 'coverage-none'))).toContain('No sampling frame is defined');
      expect(byTestId(root, 'coverage-link')?.getAttribute('href')).toBe(`/projects/${projectId}/sampling-frame`);
      expect(byTestId(root, 'coverage-table')).toBeNull();
    });

    it('tells a viewer there is none, without a link', async () => {
      const { root } = await open('viewer', none());
      expect(text(byTestId(root, 'coverage-none'))).toContain('No sampling frame is defined');
      expect(byTestId(root, 'coverage-link')).toBeNull();
      expect(root.querySelector('[data-testid="coverage-card"] a')).toBeNull();
    });
  });

  describe('coverage', () => {
    const rowTexts = (root: HTMLElement) =>
      Array.from(root.querySelectorAll('[data-testid="coverage-row"]')).map((row) => Array.from(row.querySelectorAll('td')).map((cell) => text(cell)));

    it('shows the overall progress, the codes with their labels, the target, the achieved and the percentage', async () => {
      const { root } = await open('viewer');
      expect(text(byTestId(root, 'overall-percent'))).toBe('75%');
      expect(byTestId(root, 'overall-bar')!.style.width).toBe('75%');

      const headings = Array.from(byTestId(root, 'coverage-table')!.querySelectorAll('th')).map((th) => text(th));
      expect(headings).toEqual(['Municipality', 'Sex', 'Target', 'Achieved', 'Progress']);
      const rows = rowTexts(root);
      expect(rows[0].slice(0, 4)).toEqual(['cali Cali', 'f Female', '120', '90']);
      expect(rows[0][4]).toContain('75%');
      expect(rows[1].slice(0, 2)).toEqual(['cali Cali', 'm']);
    });

    it('does not cap the percentage text, only the bar', async () => {
      const { root } = await open('viewer');
      const over = root.querySelectorAll('[data-testid="coverage-row"]')[1];
      expect(text(over.querySelector('[data-testid="row-percent"]'))).toBe('130%');
      expect(over.querySelector<HTMLElement>('[data-testid="row-bar"]')!.style.width).toBe('100%');
      const partial = root.querySelectorAll('[data-testid="coverage-row"]')[0];
      expect(partial.querySelector<HTMLElement>('[data-testid="row-bar"]')!.style.width).toBe('75%');
      expect(partial.querySelector('[role="progressbar"]')!.getAttribute('aria-valuenow')).toBe('75');
    });

    it('shows no percentage and no bar for a target of zero', async () => {
      const { root } = await open('viewer');
      const zero = root.querySelectorAll('[data-testid="coverage-row"]')[2];
      expect(text(zero.querySelectorAll('td')[2])).toBe('0');
      expect(text(zero.querySelectorAll('td')[3])).toBe('5');
      expect(text(zero.querySelector('[data-testid="row-percent"]'))).toBe('—');
      expect(zero.querySelector('[role="progressbar"]')).toBeNull();
    });

    it('shows no overall progress when no target is above zero', async () => {
      const base = dashboard();
      const { root } = await open('viewer', dashboard({ coverage: { ...base.coverage, overallPercent: null } }));
      expect(byTestId(root, 'overall-none')).not.toBeNull();
      expect(byTestId(root, 'overall-percent')).toBeNull();
      expect(byTestId(root, 'overall-bar')).toBeNull();
    });

    it('sorts by percentage with rows without one last', async () => {
      const { harness, root } = await open('viewer');
      const select = byTestId<HTMLSelectElement>(root, 'coverage-sort')!;

      select.value = 'percentAsc';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      await settle(harness);
      expect(rowTexts(root).map((r) => r[0])).toEqual(['cali Cali', 'cali Cali', 'bogota Bogotá']);
      expect(rowTexts(root).map((r) => r[3])).toEqual(['90', '130', '5']);

      select.value = 'percentDesc';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      await settle(harness);
      expect(rowTexts(root).map((r) => r[3])).toEqual(['130', '90', '5']);

      select.value = 'frame';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      await settle(harness);
      expect(rowTexts(root).map((r) => r[3])).toEqual(['90', '130', '5']);
    });

    it('pages at 50 rows', async () => {
      const base = dashboard();
      const rows = Array.from({ length: 120 }, (_, i) => ({
        values: [{ code: `c${i}`, label: null }, { code: 'f', label: null }], target: 10, achieved: i % 15, percent: ((i % 15) / 10) * 100,
      }));
      const { harness, root } = await open('viewer', dashboard({ coverage: { ...base.coverage, rows } }));
      expect(root.querySelectorAll('[data-testid="coverage-row"]')).toHaveLength(50);
      expect(text(byTestId(root, 'coverage-page-info'))).toBe('1–50 of 120');

      byTestId<HTMLButtonElement>(root, 'coverage-next')!.click();
      await settle(harness);
      expect(text(root.querySelectorAll('[data-testid="coverage-row"]')[0].querySelector('td'))).toBe('c50');
      expect(byTestId<HTMLButtonElement>(root, 'coverage-previous')!.disabled).toBe(false);
    });

    it('also renders one card per row for phones', async () => {
      const { root } = await open('viewer');
      const cards = root.querySelectorAll('[data-testid="coverage-card-item"]');
      expect(cards).toHaveLength(3);
      expect(text(cards[1])).toContain('130%');
      expect(text(cards[1])).toContain('Municipality: cali (Cali) · Sex: m');
      expect(text(cards[1])).toContain('130 / 100');
      expect(text(cards[2])).toContain('5 / 0'); // achieved / target
      expect(cards[2].querySelector('[role="progressbar"]')).toBeNull();
      expect(byTestId(root, 'coverage-cards')!.className).toContain('md:hidden');
    });

    it('shows no warnings when every dimension can be counted', async () => {
      const { root } = await open('viewer');
      expect(byTestId(root, 'coverage-warnings')).toBeNull();
    });

    it.each<[string, string]>([
      ['missingField', 'The dimension Municipality maps to municipality, a field the form no longer has.'],
      ['repeat', 'The dimension Municipality cannot be counted because its question (municipality) is answered once per repeat.'],
      ['multipleChoice', 'The dimension Municipality cannot be counted because an answer to municipality can hold several choices.'],
    ])('warns about a dimension with the %s problem and still shows the rows', async (warning, message) => {
      const base = dashboard();
      const dimensions = [{ name: 'Municipality', field: 'municipality', warning: warning as 'missingField' }, base.coverage.dimensions[1]];
      const { root } = await open('viewer', dashboard({ coverage: { ...base.coverage, dimensions } }));

      const alert = byTestId(root, 'coverage-warnings')!;
      expect(alert.getAttribute('class')).toContain('border-warning-subtle');
      expect(text(alert)).toContain(message);
      expect(alert.querySelectorAll('li')).toHaveLength(1);
      expect(byTestId(root, 'coverage-table')).not.toBeNull();
    });

    it('lists every warned dimension', async () => {
      const base = dashboard();
      const dimensions = [
        { name: 'Municipality', field: 'municipality', warning: 'repeat' as const },
        { name: 'Sex', field: 'sex', warning: 'multipleChoice' as const },
      ];
      const { root } = await open('viewer', dashboard({ coverage: { ...base.coverage, dimensions } }));
      expect(byTestId(root, 'coverage-warnings')!.querySelectorAll('li')).toHaveLength(2);
    });

    it('counts the submissions outside the frame and lists the most frequent combinations', async () => {
      const { root } = await open('viewer');
      expect(text(byTestId(root, 'outside-count'))).toBe('40');
      const items = root.querySelectorAll('[data-testid="outside-item"]');
      expect(items).toHaveLength(1);
      expect(Array.from(items[0].querySelectorAll('td')).map((cell) => text(cell))).toEqual(['05001', 'f Female', '40']);
      expect(Array.from(byTestId(root, 'outside-list')!.querySelectorAll('th')).map((th) => text(th))).toEqual(['Municipality', 'Sex', 'Submissions']);
    });

    it('shows an empty answer outside the frame', async () => {
      const base = dashboard();
      const outsideTop = [{ values: [null, { code: 'f', label: null }], submissions: 3 }];
      const { root } = await open('viewer', dashboard({ coverage: { ...base.coverage, outsideFrame: 3, outsideTop } }));
      expect(Array.from(byTestId(root, 'outside-item')!.querySelectorAll('td')).map((cell) => text(cell))).toEqual(['(empty)', 'f', '3']);
    });

    it('omits the list when nothing is outside the frame', async () => {
      const base = dashboard();
      const { root } = await open('viewer', dashboard({ coverage: { ...base.coverage, outsideFrame: 0, outsideTop: [] } }));
      expect(text(byTestId(root, 'outside-count'))).toBe('0');
      expect(byTestId(root, 'outside-list')).toBeNull();
    });

    it('shows labels and codes as text, never as markup', async () => {
      const base = dashboard();
      const rows = [{ values: [{ code: '<b>x</b>', label: '<script>alert(1)</script>' }, { code: 'f', label: null }], target: 1, achieved: 1, percent: 100 }];
      const { root } = await open('viewer', dashboard({ coverage: { ...base.coverage, rows } }));
      const table = byTestId(root, 'coverage-table')!;
      expect(table.querySelector('b, script')).toBeNull();
      expect(text(table.querySelector('tbody td'))).toBe('<b>x</b> <script>alert(1)</script>');
    });
  });

  describe('headline strip and toolbar', () => {
    it('opens with the three headline figures, each shown once', async () => {
      const { root } = await open('viewer');
      const strip = byTestId(root, 'headline')!;
      expect(text(strip)).toContain('Submissions');
      expect(text(strip)).toContain('Median completion time');
      expect(text(strip)).toContain('Overall progress');
      expect(strip.contains(byTestId(root, 'total'))).toBe(true);
      expect(strip.contains(byTestId(root, 'median'))).toBe(true);
      expect(strip.contains(byTestId(root, 'overall-percent'))).toBe(true);
      expect(root.querySelectorAll('[data-testid="total"]')).toHaveLength(1);
      expect(root.querySelectorAll('[data-testid="median"]')).toHaveLength(1);
    });

    it('shows a dash and the reason in the strip when the project has no sampling frame', async () => {
      const none = dashboard({ coverage: { state: 'notConfigured', dimensions: [], rows: [], overallPercent: null, outsideFrame: 0, outsideTop: [] } });
      const { root } = await open('viewer', none);
      expect(text(byTestId(root, 'overall-unavailable'))).toBe('—');
      expect(text(byTestId(root, 'headline'))).toContain('No sampling frame');
    });

    it('groups the two dates in a labelled fieldset', async () => {
      const { root } = await open('viewer');
      const fieldset = root.querySelector('fieldset')!;
      expect(text(fieldset.querySelector('legend'))).toBe('Date range');
      expect(fieldset.contains(byTestId(root, 'range-from'))).toBe(true);
      expect(fieldset.contains(byTestId(root, 'range-to'))).toBe(true);
    });

    it('keeps Refresh and Clear as equal secondary buttons, with no card around the toolbar', async () => {
      const { root } = await open('viewer');
      expect(byTestId(root, 'refresh')!.className).toContain('bg-neutral-secondary-medium');
      expect(byTestId(root, 'range-form')!.closest('tmz-card')).toBeNull();
    });

    it('writes the computation time to the minute, without seconds', async () => {
      const { root } = await open('viewer', dashboard({ computedAt: '2026-10-02T15:42:37Z' }));
      expect(text(byTestId(root, 'computed-at'))).toMatch(/2026, \d{1,2}:42 [AP]M$/);
    });

    it('shows the count without a reception time as a footnote that still reads when it is zero', async () => {
      const { root } = await open('viewer', dashboard({ submissions: { total: 3, withoutReceptionTime: 0, days: [] } }));
      expect(text(byTestId(root, 'without-time'))).toBe('0');
      expect(text(byTestId(root, 'submissions-card'))).toContain('Without a reception time');
    });

    it('lists the outside-the-frame combinations after the targets table', async () => {
      const { root } = await open('viewer');
      const targets = byTestId(root, 'coverage-table')!;
      const outside = byTestId(root, 'outside-list')!;
      expect(targets.compareDocumentPosition(outside) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });

    it('summarises an enumerator on a phone in two lines', async () => {
      const { root } = await open('viewer');
      const card = root.querySelector('[data-testid="enumerator-card"]')!;
      expect(card.querySelectorAll('p')).toHaveLength(2);
      expect(text(card.querySelectorAll('p')[1])).toBe('8 submissions · mean 1 h 05 min · outliers 1 · last Sep 3, 2026');
    });
  });
});
