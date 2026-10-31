import { ApplicationRef, EnvironmentInjector, createEnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, WidgetConfig } from '../../../core/projects/indicators.service';
import { DashboardPrint, printDeadlineMs, printTitle, printingClass } from './dashboard-print';
import { all, cell, clearOverlay, config, indicator, isQuery, open, paperOn, seriesResult, text, widget } from './indicators-testing';

// Paper mode (dashboard printing, design D1, D5, D6, D9, D10). jsdom lays nothing out and never prints, so these check the steps,
// their order and the waiting; the sheets themselves are checked in a real browser (tasks, group 7).

const view = document.defaultView!;

function create() {
  const injector = createEnvironmentInjector([DashboardPrint], TestBed.inject(EnvironmentInjector));
  return { print: injector.get(DashboardPrint), injector };
}

/** Fake timers for the wait: its polling, its deadline and the two frames it waits for (where jsdom has frames). */
function fakeTimers() {
  const frames = typeof requestAnimationFrame === 'function' ? (['requestAnimationFrame', 'cancelAnimationFrame'] as const) : [];
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date', ...frames] });
}

describe('DashboardPrint', () => {
  beforeEach(() => {
    document.title = 'Tamiza';
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  it('lays the dashboard out as it prints: the class, the page rule and the title, then measures between two renders', () => {
    const { print } = create();
    const steps: string[] = [];
    vi.spyOn(TestBed.inject(ApplicationRef), 'tick').mockImplementation(() => void steps.push('render'));
    print.registerMeasure(() => steps.push('chart'));
    print.registerMeasure(() => steps.push('map'));
    print.titleSource = () => 'Census 2026 – Housing – 2026-10-28';

    print.prepare();

    expect(document.documentElement.classList).toContain(printingClass);
    expect(document.getElementById('tmz-print-page')?.textContent).toBe('@page { margin: 10mm; }');
    expect(document.title).toBe('Census 2026 – Housing – 2026-10-28');
    expect(steps).toEqual(['render', 'chart', 'map', 'render']);
    expect(print.printing()).toBe(true);
    expect(print.printedAt()).toBeInstanceOf(Date);
  });

  it('measures only what is still registered', () => {
    const { print } = create();
    const measured: string[] = [];
    const off = print.registerMeasure(() => measured.push('gone'));
    print.registerMeasure(() => measured.push('kept'));
    off();

    print.prepare();

    expect(measured).toEqual(['kept']);
  });

  it('undoes every step on restore, once, and says paper mode ended', () => {
    const { print } = create();
    const restored: { fromAction: boolean }[] = [];
    print.restored.subscribe((event) => restored.push(event));
    print.titleSource = () => 'Census 2026 – Housing – 2026-10-28';
    print.prepare();

    print.restore();
    print.restore();

    expect(document.documentElement.classList).not.toContain(printingClass);
    expect(document.getElementById('tmz-print-page')).toBeNull();
    expect(document.title).toBe('Tamiza');
    expect(print.printing()).toBe(false);
    expect(print.printedAt()).toBeNull();
    expect(restored).toEqual([{ fromAction: false }]);
  });

  it('waits while a widget is pending, then resolves once it is not', async () => {
    fakeTimers();
    const { print } = create();
    const loading = signal(true);
    print.registerPending(loading);
    print.prepare();
    let ready: boolean | undefined;
    void print.whenReady().then((value) => (ready = value));

    await vi.advanceTimersByTimeAsync(5000);
    expect(ready).toBeUndefined();

    loading.set(false);
    await vi.advanceTimersByTimeAsync(200);
    expect(ready).toBe(true);
  });

  it('stops waiting at the deadline, so a widget that never loads does not hold the print back', async () => {
    fakeTimers();
    const { print } = create();
    print.registerPending(signal(true));
    print.prepare();
    let ready: boolean | undefined;
    void print.whenReady().then((value) => (ready = value));

    await vi.advanceTimersByTimeAsync(printDeadlineMs - 100);
    expect(ready).toBeUndefined();

    await vi.advanceTimersByTimeAsync(300);
    expect(ready).toBe(true);
  });

  it('answers false when paper mode ends while it waits (the page was left)', async () => {
    fakeTimers();
    const { print } = create();
    print.registerPending(signal(true));
    print.prepare();
    let ready: boolean | undefined;
    void print.whenReady().then((value) => (ready = value));

    print.restore();
    await vi.advanceTimersByTimeAsync(200);

    expect(ready).toBe(false);
  });

  it('prepares on beforeprint, only when not printing yet, and restores on afterprint (the browser\'s own command)', () => {
    const { print } = create();
    const measure = vi.fn();
    print.registerMeasure(measure);

    view.dispatchEvent(new Event('beforeprint'));
    view.dispatchEvent(new Event('beforeprint'));
    expect(print.printing()).toBe(true);
    expect(measure).toHaveBeenCalledTimes(1);

    view.dispatchEvent(new Event('afterprint'));
    expect(print.printing()).toBe(false);
  });

  it('opens the dialog once ready, after `opening`, and restores from the Print action when it closes', async () => {
    const { print } = create();
    const steps: string[] = [];
    const restored: { fromAction: boolean }[] = [];
    print.restored.subscribe((event) => restored.push(event));
    // A browser whose dialog blocks, as Chrome's does: afterprint comes before print() returns.
    vi.spyOn(view, 'print').mockImplementation(() => {
      steps.push(`dialog (printing: ${print.printing()})`);
      view.dispatchEvent(new Event('afterprint'));
    });

    await print.print(() => steps.push('opening'));

    expect(steps).toEqual(['opening', 'dialog (printing: true)']);
    expect(print.printing()).toBe(false);
    expect(restored).toEqual([{ fromAction: true }]);
  });

  it('restores on the first pointer or key on the page when the dialog neither blocks nor says it closed', async () => {
    const { print } = create();
    vi.spyOn(view, 'print').mockImplementation(() => undefined);

    await print.print();
    expect(print.printing()).toBe(true);

    view.dispatchEvent(new Event('keydown'));
    expect(print.printing()).toBe(false);
  });

  it('opens no dialog when paper mode ended while it waited', async () => {
    fakeTimers();
    const { print } = create();
    const dialog = vi.spyOn(view, 'print').mockImplementation(() => undefined);
    print.registerPending(signal(true));

    const done = print.print();
    print.restore();
    await vi.advanceTimersByTimeAsync(200);
    await done;

    expect(dialog).not.toHaveBeenCalled();
  });

  it('restores and stops listening once destroyed', () => {
    const { print, injector } = create();
    print.prepare();

    injector.destroy();

    expect(print.printing()).toBe(false);
    expect(document.documentElement.classList).not.toContain(printingClass);
    view.dispatchEvent(new Event('beforeprint'));
    expect(print.printing()).toBe(false);
  });
});

describe('printTitle', () => {
  it('names the project, the dashboard and the day in the project\'s time zone (PDF file name)', () => {
    const late = new Date('2026-10-29T03:00:00Z');
    expect(printTitle('Census 2026', 'Housing', late, 'America/Bogota')).toBe('Census 2026 – Housing – 2026-10-28');
    expect(printTitle('Census 2026', 'Housing', late, 'UTC')).toBe('Census 2026 – Housing – 2026-10-29');
  });

  it('passes names through as text, and falls back to UTC for a zone it does not know', () => {
    const now = new Date('2026-10-29T03:00:00Z');
    expect(printTitle('A/B', '<b>Avance</b>', now, 'Mars/Olympus')).toBe('A/B – <b>Avance</b> – 2026-10-29');
  });
});

describe('the new kinds of widget on paper (dashboard printing, "Charts on paper" and "Tables on paper")', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const households = indicator('households_visited');

  async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: ReturnType<typeof seriesResult>) {
    const context = await open('viewer', [widget('w1', { chartType, config: config({ indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', ...settings }) })], null, { indicators: [households] });
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(answer);
    await settle(context.harness);
    return context;
  }

  it('prints a heat map whole: its 80 cells, its row and column labels and the legend of its scale (Heat map)', async () => {
    const names = Array.from({ length: 8 }, (_, i) => `S${i + 1}`);
    const cells = Array.from({ length: 10 }, (_, g) => names.map((name, s) => cell(`Place ${g + 1}`, name, 10 + g * 8 + s)));
    const context = await show('heatmap', {}, seriesResult(cells.flat(), names));

    await paperOn(context);

    expect(all(context.root, 'chart-mark')).toHaveLength(80);
    expect(all(context.root, 'heat-row-label')).toHaveLength(10);
    expect(all(context.root, 'heat-column-label')).toHaveLength(8);
    const legend = byTestId(context.root, 'heatmap-legend')!;
    expect(legend.closest('.paper\\:hidden')).toBeNull();
    expect(legend.querySelectorAll('[style*="--tmz-seq-"]')).toHaveLength(7);
    expect(text(byTestId(context.root, 'heatmap-legend-min'))).toBe('10 households');
    // The svg takes the box it has on paper, and scales into it rather than being cut off.
    expect(byTestId(context.root, 'chart-svg')!.getAttribute('class')).toContain('tmz-chart-svg');
    expect(byTestId(context.root, 'chart-svg')!.getAttribute('viewBox')).toMatch(/^0 0 \d+ \d+$/);
  });

  it('prints a stacked chart with every segment and a legend naming its 4 series, all of it in view (Stacked chart)', async () => {
    const series = ['Piped', 'Well', 'River', 'Truck'];
    const cells = ['Cali', 'Buga', 'Pasto'].flatMap((group, g) => series.map((name, s) => cell(group, name, 10 * (g + 1) + s)));
    const context = await show('column', { layout: 'stacked' }, seriesResult(cells, series));

    await paperOn(context);

    expect(all(context.root, 'chart-mark')).toHaveLength(12);
    expect(all(context.root, 'legend-name').map(text)).toEqual(series);
    const legend = byTestId(context.root, 'series-legend')!;
    expect(legend.className).toContain('flex-wrap'); // it wraps, never scrolls
    expect(legend.closest('.paper\\:hidden')).toBeNull();
    expect(all(context.root, 'legend-swatch').map((s) => s.getAttribute('style'))).toEqual(series.map((_, i) => expect.stringContaining(`var(--tmz-series-${i + 1})`)));
  });

  it('prints the group column and the columns that fit, and says how many it left out (Wide cross table)', async () => {
    const series = Array.from({ length: 20 }, (_, i) => `S${String(i + 1).padStart(2, '0')}`);
    const cells = ['Cali', 'Buga'].flatMap((group) => series.map((name, s) => cell(group, name, s + 1)));
    const context = await show('crosstab', {}, seriesResult(cells, series));
    // jsdom lays nothing out: the box is 400 px wide and every column 40 px, so the group column and 9 more fit.
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
      const box = { top: 0, left: 0, width: 0, height: 0, right: 0, bottom: 900 } as DOMRect;
      if (this.tagName === 'TMZ-TABLE-CONTAINER') {
        return { ...box, right: 400 };
      }
      if (this.matches('thead th')) {
        return { ...box, right: (Array.from(this.parentElement!.children).indexOf(this) + 1) * 40, bottom: 40 };
      }
      if (this.matches('tbody > tr')) {
        return { ...box, bottom: 40 + (Array.from(this.parentElement!.children).indexOf(this) + 1) * 24 };
      }
      return box;
    });

    await paperOn(context);

    const printed = (selector: string) => Array.from(context.root.querySelectorAll(selector)).filter((c) => !c.classList.contains('paper:hidden'));
    expect(printed('thead th').map(text)).toEqual(['municipality', ...series.slice(0, 9)]);
    expect(printed('tbody tr:first-child > *')).toHaveLength(10);
    const note = byTestId(context.root, 'widget-table-columns-left-out')!;
    expect(text(note)).toBe('12 columns not printed.'); // 20 series and the Total column: 21 columns, 9 printed
    expect(note.classList).toContain('paper:block');
    expect(note.classList).toContain('hidden');
    expect(byTestId(context.root, 'widget-table-left-out')).toBeNull(); // the 3 rows all fit
  });

  it('prints every column of a table whose columns all fit, with no note', async () => {
    const context = await show('crosstab', {}, seriesResult([cell('Cali', 'Piped', 1), cell('Cali', 'Well', 2)], ['Piped', 'Well']));
    vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
      const box = { top: 0, left: 0, width: 0, height: 0, right: 0, bottom: 900 } as DOMRect;
      return this.tagName === 'TMZ-TABLE-CONTAINER' ? { ...box, right: 400 } : this.matches('thead th') ? { ...box, right: (Array.from(this.parentElement!.children).indexOf(this) + 1) * 40 } : box;
    });

    await paperOn(context);

    expect(Array.from(context.root.querySelectorAll('thead th')).filter((c) => c.classList.contains('paper:hidden'))).toHaveLength(0);
    expect(byTestId(context.root, 'widget-table-columns-left-out')).toBeNull();
  });
});
