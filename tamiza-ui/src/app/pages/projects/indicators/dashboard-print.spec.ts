import { ApplicationRef, EnvironmentInjector, createEnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MapStub, stubMap } from '../../../../testing/leaflet';
import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, DashboardColors, QueryBox, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { DashboardPrint, printDeadlineMs, printTitle, printingClass } from './dashboard-print';
import { all, cell, clearOverlay, config, indicator, isQuery, open, paperOn, result, row, seriesResult, text, wait, toggleTable, widget } from './indicators-testing';
import { loadLeaflet } from './map-engine';

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
    expect(all(context.root, 'legend-swatch').map((s) => s.getAttribute('style'))).toEqual(series.map((_, i) => expect.stringContaining(`var(--tmz-pal-tamiza-${i + 1})`)));
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

describe('the dashboard\'s colors on paper (dashboard printing, "Colors on paper")', () => {
  let stub: MapStub;

  beforeAll(async () => {
    const preload = stubMap();
    await loadLeaflet();
    preload.restore();
  });

  beforeEach(() => {
    stub = stubMap({ width: 600, height: 400 });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    stub.restore();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const households = indicator('households_visited');
  const square = (x: number) => `POLYGON ((${x} 0, ${x + 1} 0, ${x + 1} 1, ${x} 1, ${x} 0))`;
  const colorOf = (element: Element | null) => /var\(--[\w-]+\)/.exec(element?.getAttribute('style') ?? '')?.[0] ?? null;

  /** "Housing": the palette Earth, Well kept in color 6; a column chart in color 3, a stacked chart by water source and a map on the green scale. */
  async function housing() {
    const colors: DashboardColors = { palette: 'earth', valueColors: [{ column: 'water_source', value: 'Well', color: 6 }] };
    const widgets = [
      widget('w1', { chartType: 'column', config: config({ indicator: 'households_visited', groupBy: 'municipality', display: { color: 3 } }), layout: { x: 0, y: 0, w: 4, h: 4 } }),
      widget('w2', { chartType: 'column', config: config({ indicator: 'households_visited', groupBy: 'municipality', seriesBy: 'water_source', layout: 'stacked' }), layout: { x: 4, y: 0, w: 4, h: 4 } }),
      widget('w3', { chartType: 'map', config: config({ indicator: 'households_visited', geometryColumn: 'geometry', display: { ramp: 'green' } }), layout: { x: 8, y: 0, w: 4, h: 4 } }),
    ];
    const answer = (body: { config: WidgetConfig; chartType?: string }): QueryResult =>
      body.chartType === 'map'
        ? result([row('Cali', 90, square(0)), row('Pasto', 10, square(1))])
        : body.config.seriesBy
          ? seriesResult([cell('Cali', 'Piped', 35), cell('Cali', 'Well', 15)], ['Piped', 'Well'])
          : result([row('Cali', 75), row('Pasto', 35)]);
    const context = await open('viewer', widgets, answer, { indicators: [households] }, { colors });
    context.http.match((r) => r.method === 'GET' && r.url === '/api/v1/basemaps').forEach((r) => r.flush({ provider: 'none', basemaps: [] }));
    for (let i = 0; i < 20 && !context.root.querySelector('.leaflet-container path'); i++) {
      await wait(5);
      await settle(context.harness);
    }
    return context;
  }

  /** What the print shows of the colors: the marks of each chart, the legend's swatches and the variables the map sets for its scale. */
  function colorsPrinted(root: HTMLElement) {
    const map = root.querySelector<HTMLElement>('app-map-widget')!;
    return {
      columns: all(root, 'chart-mark').map(colorOf),
      swatches: all(root, 'legend-swatch').map(colorOf),
      mapSteps: [1, 2, 3, 4, 5, 6, 7].map((n) => map.style.getPropertyValue(`--tmz-seq-${n}`)),
      mapLegend: Array.from(byTestId(root, 'map-legend')!.querySelectorAll('[style*="--tmz-seq-"]')).map(colorOf),
      shapes: all(root, 'map-region').map((shape) => Array.from(shape.classList).find((c) => c.startsWith('tmz-seq-'))),
    };
  }

  it('prints the charts in the colors of the dashboard\'s palette and the map on its scale, as on screen (Palette printed)', async () => {
    const context = await housing();
    const onScreen = colorsPrinted(context.root);

    await paperOn(context);
    const onPaper = colorsPrinted(context.root);

    expect(onPaper).toEqual(onScreen);
    // The column chart in color 3 of Earth, the stacked chart in the first color of Earth and the one kept for Well, color 6.
    expect(onPaper.columns).toEqual([...Array(2).fill('var(--tmz-pal-earth-3)'), 'var(--tmz-pal-earth-1)', 'var(--tmz-pal-earth-6)']);
    expect(onPaper.swatches).toEqual(['var(--tmz-pal-earth-1)', 'var(--tmz-pal-earth-6)']);
    // The map is on the green scale: it sets the steps its shapes and its legend read, and none of them is Earth's own scale.
    expect(onPaper.mapSteps).toEqual([1, 2, 3, 4, 5, 6, 7].map((n) => `var(--tmz-ramp-green-${n})`));
    expect(onPaper.mapLegend).toEqual([1, 2, 3, 4, 5, 6, 7].map((n) => `var(--tmz-seq-${n})`));
    expect(onPaper.shapes).toHaveLength(2);
    // Nothing of the print sheet hides the legends or the swatches.
    expect(byTestId(context.root, 'map-legend')!.closest('.paper\\:hidden')).toBeNull();
    expect(byTestId(context.root, 'series-legend')!.closest('.paper\\:hidden')).toBeNull();
  });

  it('draws the screen again, in the same colors, once the print is over', async () => {
    const context = await housing();
    const onScreen = colorsPrinted(context.root);
    const print = await paperOn(context);

    print.restore();
    await settle(context.harness);

    expect(colorsPrinted(context.root)).toEqual(onScreen);
  });

  describe('a box plot', () => {
    const box = (group: string, median: number, extra: Partial<QueryBox> = {}): QueryBox => ({
      group, values: 12, min: median - 3, q1: median - 1, median, q3: median + 1, max: median + 3, mean: median, lowerWhisker: median - 3, upperWhisker: median + 3, outlierCount: 0, outliers: [], ...extra,
    });
    const answer = (boxes: QueryBox[]): QueryResult => result(boxes.map((b) => row(b.group, b.median, null, { rows: b.values })), { boxes });

    async function showBoxes(boxes: QueryBox[], settings: Partial<WidgetConfig> = {}) {
      const context = await open('viewer', [widget('w1', { chartType: 'boxplot', config: config({ indicator: 'households_visited', groupBy: 'municipality', ...settings }) })], null, { indicators: [households] });
      const requests = context.http.match(isQuery);
      expect(requests).toHaveLength(1);
      requests[0].flush(answer(boxes));
      await settle(context.harness);
      return context;
    }

    it('prints whole: its boxes, medians, means, whiskers and outliers, its labels and its key, with the note on the outliers it did not draw (Box plot)', async () => {
      const outliers = Array.from({ length: 100 }, (_, i) => 1000 + i);
      const context = await showBoxes(
        [box('Cali', 20, { outlierCount: 130, outliers, max: 1200 }), box('Buga', 30), box('Pasto', 40), box('Tumaco', 50), box('Quibdó', 60), box('Leticia', 70)],
        { display: { orientation: 'horizontal' } },
      );

      await paperOn(context);

      expect(all(context.root, 'chart-mark')).toHaveLength(6);
      expect(all(context.root, 'box-median')).toHaveLength(6);
      expect(all(context.root, 'box-mean')).toHaveLength(12);
      expect(all(context.root, 'box-outlier')).toHaveLength(100);
      const key = byTestId(context.root, 'boxplot-key')!;
      expect(key.closest('.paper\\:hidden')).toBeNull();
      expect(text(key)).toContain('Middle half (Q1 to Q3)');
      expect(text(key)).toContain('Outliers');
      const note = byTestId(context.root, 'boxplot-not-drawn')!;
      expect(note.closest('.paper\\:hidden')).toBeNull();
      expect(text(note)).toBe('30 outliers not drawn');
      expect(byTestId(context.root, 'chart-svg')!.getAttribute('class')).toContain('tmz-chart-svg');
      expect(byTestId(context.root, 'chart-svg')!.getAttribute('viewBox')).toMatch(/^0 0 \d+ \d+$/);
    });

    it('prints the rows of its table that fit and says how many it left out when it is shown as a table (Box plot as a table)', async () => {
      const boxes = Array.from({ length: 10 }, (_, i) => box(`Place ${i + 1}`, 10 + i));
      const context = await showBoxes(boxes);
      await toggleTable(context);
      await settle(context.harness);
      const table = context.root.querySelector('app-widget-table')!;
      vi.spyOn(table, 'getBoundingClientRect').mockReturnValue({ bottom: 130 } as DOMRect);
      table.querySelectorAll<HTMLElement>('tbody > tr').forEach((tr, i) => vi.spyOn(tr, 'getBoundingClientRect').mockReturnValue({ bottom: 40 + (i + 1) * 24 } as DOMRect));

      await paperOn(context);

      const printedRows = Array.from(table.querySelectorAll('tbody > tr')).filter((tr) => !tr.classList.contains('paper:hidden'));
      expect(printedRows).toHaveLength(2);
      expect(text(printedRows[0].children[0])).toBe('Place 1');
      expect(text(byTestId(context.root, 'widget-table-left-out'))).toBe('8 rows not printed.');
      // The table's nine columns are all in it.
      expect(Array.from(table.querySelectorAll('thead th')).map(text)).toEqual(['municipality', 'Values', 'Minimum', 'Q1', 'Median', 'Q3', 'Maximum', 'Mean', 'Outliers']);
    });
  });

});
