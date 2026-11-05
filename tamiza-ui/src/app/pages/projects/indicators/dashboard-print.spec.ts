import { ApplicationRef, EnvironmentInjector, createEnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MapStub, stubMap } from '../../../../testing/leaflet';
import { byTestId, settle } from '../../../../testing/project-harness';
import { ChartType, DashboardColors, QueryBox, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { DashboardPrint, printDeadlineMs, printTitle, printingClass } from './dashboard-print';
import { all, cell, clearOverlay, config, indicator, isQuery, movedElements, open, paperOn, result, row, seriesResult, text, wait, toggleTable, widget } from './indicators-testing';
import { boxOf as boxOfText, overlaps } from './chart-annotations';
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

describe('column names on paper (add-indicator-column-metadata, "Column labels on paper")', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const teatro = indicator('coverage', {
    label: 'Teatro hoy',
    columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' }, municipality: { label: 'Comuna' } },
  });

  async function show() {
    const ratio = { numerator: 'numerator', denominator: 'denominator' };
    const context = await open('viewer', [widget('w1', { chartType: 'column', config: config({ ratio }) })], null, { indicators: [teatro] });
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(result([row('Zona 1', 0.5), row('Zona 2', 0.25)]));
    await settle(context.harness);
    return context;
  }

  it('prints the caption of a chart with the names it has on screen (Caption printed)', async () => {
    const context = await show();
    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares by Comuna');

    await paperOn(context);

    expect(text(byTestId(context.root, 'chart-caption'))).toBe('Teatro hoy: Hogares que asistieron / Hogares by Comuna');
    expect(byTestId(context.root, 'chart-caption')!.closest('.paper\:hidden')).toBeNull();
  });

  it('prints the headers of a table with the names it has on screen (Table printed)', async () => {
    const context = await show();
    await toggleTable(context);
    await settle(context.harness);

    await paperOn(context);

    expect(Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text)).toEqual(['Comuna', 'Teatro hoy: Hogares que asistieron / Hogares']);
  });
});

describe('the words around a widget on paper (add-widget-context, "Subtitle on paper", "Charts on paper")', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const subtitle = '¿En qué lugares ha visto teatro en el último año? (E4), preguntado a quienes asistieron a alguna función en las zonas visitadas';
  const footnote = 'No suman 100 %: respuesta múltiple. '.repeat(10).trim();
  const described = indicator('coverage', { description: 'Share of households that visited a theatre in the last year.' });

  async function show(display: NonNullable<WidgetConfig['display']>, indicators = [indicator('coverage')]) {
    const context = await open('viewer', [widget('w1', { chartType: 'column', config: config({ display }) })], null, { indicators });
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(result([row('Zona 1', 0.5), row('Zona 2', 0.25)]));
    await settle(context.harness);
    return context;
  }

  it('prints the subtitle and the footnote whole, and measures the chart again for the room they leave (Whole texts on paper)', async () => {
    const context = await show({ subtitle, footnote });
    const widthBefore = byTestId(context.root, 'chart-svg')!.getAttribute('viewBox');
    // jsdom lays nothing out: every box is 333 by 222 once the sheet is laid out.
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 333, height: 222, x: 0, y: 0, top: 0, left: 0, right: 333, bottom: 222, toJSON: () => ({}) });

    await paperOn(context);

    expect(byTestId(context.root, 'widget-subtitle')!.classList).toContain('paper:line-clamp-none');
    expect(byTestId(context.root, 'widget-footnote')!.classList).toContain('paper:line-clamp-none');
    expect(text(byTestId(context.root, 'widget-subtitle'))).toBe(subtitle);
    expect(text(byTestId(context.root, 'widget-footnote'))).toBe(footnote);
    expect(byTestId(context.root, 'widget-subtitle')!.closest('.paper\:hidden')).toBeNull();
    expect(byTestId(context.root, 'widget-footnote')!.closest('.paper\:hidden')).toBeNull();
    expect(byTestId(context.root, 'chart-svg')!.getAttribute('viewBox')).toBe('0 0 333 222');
    expect(widthBefore).not.toBe('0 0 333 222');
  });

  it('prints a widget with a hidden caption without a visible caption line, as on screen (Hidden caption on paper)', async () => {
    const context = await show({ caption: { hidden: true } });
    expect(byTestId(context.root, 'chart-caption')!.classList).toContain('sr-only');

    await paperOn(context);

    const caption = byTestId(context.root, 'chart-caption')!;
    expect(caption.classList).toContain('sr-only');
    expect(text(caption)).toBe('Coverage by municipality'); // still in the page, for the tools that read it
    expect(caption.closest('.paper\:hidden')).toBeNull();
  });

  it('does not print the description button, whatever the widget offers (Description not printed)', async () => {
    const context = await show({ showDescription: true }, [described]);
    const button = byTestId(context.root, 'widget-description')!;
    expect(button).not.toBeNull();

    await paperOn(context);

    expect(button.closest('app-description-popover')!.classList).toContain('paper:hidden');
    expect(context.root.textContent).not.toContain('Share of households');
  });

  it('cuts the chart at its own box when the texts leave it too little room, never over the footnote (Texts that leave a chart too little room)', async () => {
    const context = await show({ footnote });

    await paperOn(context);

    const box = byTestId(context.root, 'widget-footnote')!.previousElementSibling as HTMLElement;
    expect(box.classList).toContain('overflow-hidden');
    expect(box.contains(byTestId(context.root, 'chart-svg'))).toBe(true);
    expect(box.contains(byTestId(context.root, 'widget-footnote'))).toBe(false);
  });

  it('keeps the chart box of a widget without a footnote as it is on paper', async () => {
    const context = await show({ subtitle });

    await paperOn(context);

    expect(byTestId(context.root, 'widget-footnote')).toBeNull();
    const box = byTestId(context.root, 'chart-svg')!.closest('.min-h-0.flex-1') as HTMLElement;
    expect(box.classList).not.toContain('overflow-hidden');
  });
});

describe('chart annotations on paper (add-chart-annotations, "Chart annotations on paper")', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  /** jsdom lays nothing out: every box is this size, once it is mocked. */
  const boxOf = (width: number, height: number) =>
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width, height, x: 0, y: 0, top: 0, left: 0, right: width, bottom: height, toJSON: () => ({}) });

  async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult, indicators = [indicator('piped_water_rate')]) {
    const context = await open('viewer', [widget('w1', { chartType, config: config({ indicator: 'piped_water_rate', ...settings }) })], null, { indicators });
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(answer);
    await settle(context.harness);
    return context;
  }

  const zones = result([row('Zona 1', 0.5), row('Zona 2', 0.4), row('Zona 3', 0.6), row('Zona 4', 0.2)], { overall: 0.4599 });

  it('prints both dashed lines across the columns and the key that lists them, inside the widget\'s box (Reference lines printed)', async () => {
    const context = await show('column', { ratio, display: { referenceLines: [{ kind: 'mean' }, { kind: 'overall' }] } }, zones);
    boxOf(333, 222);

    await paperOn(context);

    const lines = all(context.root, 'reference-line');
    expect(lines).toHaveLength(2);
    expect(lines.map((line) => line.getAttribute('stroke-dasharray'))).toEqual(['6 4', '2 3']);
    expect(lines.every((line) => Number(line.getAttribute('x1')) < Number(line.getAttribute('x2')) && Number(line.getAttribute('x2')) <= 333)).toBe(true);
    const key = byTestId(context.root, 'reference-key')!;
    expect(all(context.root, 'reference-key-text').map(text)).toEqual(['Mean: 42.5%', 'Overall: 45.99%']);
    expect(key.closest('.paper\:hidden')).toBeNull();
    // The key is the chart's own, a sibling of the plot's box (it takes its room before the plot does) and in the widget's box.
    const host = key.closest('app-reference-key')!;
    expect(host.classList).toContain('shrink-0');
    expect(host.previousElementSibling!.contains(byTestId(context.root, 'chart-svg'))).toBe(true);
    expect(key.closest('app-widget-view')).not.toBeNull();
    expect(byTestId(context.root, 'chart-svg')!.getAttribute('viewBox')).toBe('0 0 333 222');
  });

  const barsOf = (count: number) => result(Array.from({ length: count }, (_, i) => row(`Zona ${i + 1}`, 0.1 + i / 100)));

  /** Whether any two texts of the plot cover each other. */
  const overlapping = (context: Awaited<ReturnType<typeof show>>) => {
    const labels = all(context.root, 'data-label').map((label) => ({
      x: Number(label.getAttribute('x')),
      y: Number(label.getAttribute('y')),
      text: label.textContent ?? '',
      anchor: label.getAttribute('text-anchor') as 'start' | 'middle' | 'end',
    }));
    return labels.some((a, i) => labels.slice(i + 1).some((b) => overlaps(boxOfText(a), boxOfText(b))));
  };

  it('prints each data label that fits its bar on paper, and none overlaps another (Data labels on paper)', async () => {
    // On screen the 30 bars are thin and have no label; the sheet gives them room, so they are laid out again and printed.
    const context = await show('bar', { display: { dataLabels: true } }, barsOf(30));
    expect(all(context.root, 'data-label')).toHaveLength(0);
    boxOf(333, 900);

    await paperOn(context);

    expect(all(context.root, 'data-label')).toHaveLength(30);
    expect(overlapping(context)).toBe(false);
  });

  it('leaves out of the print the labels of bars that are thinner on paper than on screen (Data labels on paper)', async () => {
    const tall = boxOf(333, 600);
    const context = await show('bar', { display: { dataLabels: true } }, barsOf(12));
    expect(all(context.root, 'data-label')).toHaveLength(12);
    tall.mockRestore();
    boxOf(333, 130);

    await paperOn(context);

    expect(all(context.root, 'data-label')).toHaveLength(0);
    expect(all(context.root, 'chart-mark')).toHaveLength(12);
  });

  it('prints a line chart drawn with steps as steps, not as straight segments (Steps printed)', async () => {
    const context = await show('line', { display: { interpolation: 'step' } }, result([row('$0', 0.98), row('$10.000', 0.6), row('$20.000', 0.3)]));
    boxOf(333, 222);

    await paperOn(context);

    const path = byTestId(context.root, 'chart-line')!.getAttribute('d')!;
    expect(path).toMatch(/^M[^HVL]+H[^HVL]+V[^HVL]+H[^HVL]+V[^HVL]+$/);
    expect(path).not.toContain('L');
  });
});

describe('the declared category order on paper (add-declared-category-order, "Declared category order on paper")', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const teatro = indicator('teatro_conteo', {
    label: 'Teatro conteo',
    columns: { comuna: { order: ['COMUNA 2', 'COMUNA 1'], last: ['Sin dato'] }, respuesta: { order: ['1. Sí', '2. No'], stripPrefix: true } },
  });
  const answer = () =>
    seriesResult(
      [cell('COMUNA 1', '2. No', 10), cell('COMUNA 1', '1. Sí', 30), cell('Sin dato', '1. Sí', 3), cell('COMUNA 2', '1. Sí', 20), cell('COMUNA 2', '2. No', 25)],
      ['2. No', '1. Sí'],
    );

  async function show(chartType: ChartType) {
    const context = await open('viewer', [widget('w1', { chartType, config: config({ indicator: 'teatro_conteo', groupBy: 'comuna', seriesBy: 'respuesta', measure: 'value', layout: chartType === 'column' ? 'stacked' : undefined }) })], null, { indicators: [teatro] });
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(answer());
    await settle(context.harness);
    return context;
  }

  it('prints a heat map with its rows in the declared order and its column labels without their numbers (Heat map in a declared order)', async () => {
    const context = await show('heatmap');

    await paperOn(context);

    expect(all(context.root, 'heat-row-label').map(text)).toEqual(['COMUNA 2', 'COMUNA 1', 'Sin dato']);
    expect(all(context.root, 'heat-column-label').map(text)).toEqual(['Sí', 'No']);
  });

  it('prints a column chart shown as a table with its groups in the declared order (Table in a declared order)', async () => {
    const context = await show('column');
    await toggleTable(context);
    await settle(context.harness);

    await paperOn(context);

    const rows = all(context.root, 'widget-table-row').map((r) => text(r.children[0]));
    expect(rows.slice(0, 3)).toEqual(['COMUNA 2', 'COMUNA 1', 'Sin dato']);
    expect(Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text).slice(0, 3)).toEqual(['comuna', 'Sí', 'No']);
  });
});

describe('small bases on paper (add-small-sample-warnings, "Small bases on paper")', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    clearOverlay();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  const ratio = { numerator: 'numerator', denominator: 'denominator' };
  const teatro = indicator('teatro_hoy', {
    label: 'Teatro hoy',
    format: 'percent',
    columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' } },
  });
  const comunas = () => [
    row('COMUNA 7', 0.5, null, { numerator: 8, denominator: 16, n: 16 }),
    row('COMUNA 1', 56 / 121, null, { numerator: 56, denominator: 121, n: 121 }),
    row('SIN NOMBRE', 0.25, null, { numerator: 5, denominator: 20, n: 20 }),
  ];
  const footnote = 'No suman 100 %: respuesta múltiple.';

  async function show(chartType: ChartType, settings: Partial<WidgetConfig>, answer: QueryResult = result(comunas(), { n: 157 })) {
    const context = await open('viewer', [widget('w1', { chartType, config: config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', ...settings }) })], null, { indicators: [teatro] });
    const requests = context.http.match(isQuery);
    expect(requests).toHaveLength(1);
    requests[0].flush(answer);
    await settle(context.harness);
    return context;
  }

  it('prints the dashes of the dimmed columns and the lines under the chart, whole, before the footnote (Printed in black and white)', async () => {
    const context = await show('column', { display: { minN: 30, showN: true, footnote } }, result(comunas(), { n: 157, noData: { values: ['No sabe'], value: 0.0224 } }));

    await paperOn(context);

    // Black and white keeps what reads without color: the dashed outline, and the lighter fill.
    const marks = all(context.root, 'chart-mark');
    expect(marks.map((mark) => mark.getAttribute('stroke-dasharray'))).toEqual(['3 2', null, '3 2']);
    expect(marks.map((mark) => mark.getAttribute('fill-opacity'))).toEqual(['0.35', null, '0.35']);
    // The lines print whole, in their order, above the footnote.
    const lines = ['widget-small-note', 'widget-n-line', 'widget-no-data-line', 'widget-footnote'].map((id) => byTestId(context.root, id)!);
    expect(lines.map(text)).toEqual(['Marks with a base below 30 are dimmed.', 'n = 157 Hogares', 'No sabe: 2.24%', footnote]);
    for (const line of lines) {
      expect(line.classList).toContain('paper:line-clamp-none');
      expect(line.closest('.paper\:hidden')).toBeNull();
    }
    lines.slice(1).forEach((line, i) => expect(lines[i].compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy());
    expect(byTestId(context.root, 'chart-svg')!.querySelector(movedElements)).toBeNull();
  });

  it('cuts the chart at its own box when a line leaves it too little room, never over the line', async () => {
    const context = await show('column', { display: { showN: true } });

    await paperOn(context);

    const box = byTestId(context.root, 'widget-n-line')!.previousElementSibling as HTMLElement;
    expect(box.classList).toContain('overflow-hidden');
    expect(box.contains(byTestId(context.root, 'chart-svg'))).toBe(true);
    expect(box.contains(byTestId(context.root, 'widget-n-line'))).toBe(false);
  });

  it('prints the hatch of a heat map inside the chart\'s own SVG, so it prints with the chart', async () => {
    const cells = [
      { ...cell('A', 'x', 0.1), numerator: 1, denominator: 10, n: 10 },
      { ...cell('A', 'y', 0.5), numerator: 50, denominator: 100, n: 100 },
    ];
    const context = await show('heatmap', { seriesBy: 'categoria', display: { minN: 30 } }, seriesResult(cells, ['x', 'y']));

    await paperOn(context);

    const svg = byTestId(context.root, 'chart-svg')!;
    const small = svg.querySelector('[data-testid="heat-small"]')!;
    expect(small).not.toBeNull();
    const pattern = small.getAttribute('fill')!.match(/url\(#(.+)\)/)![1];
    expect(svg.querySelector(`pattern[id="${pattern}"]`)).not.toBeNull();
    expect(small.getAttribute('stroke-dasharray')).toBe('3 2');
    expect(text(byTestId(context.root, 'widget-small-note'))).toBe('Marks with a base below 30 are dimmed.');
  });

  it('prints the table of a widget with its n column, its marks and the note that explains them', async () => {
    const context = await show('column', { display: { minN: 30 } });
    await toggleTable(context);
    await settle(context.harness);

    await paperOn(context);

    expect(Array.from(context.root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text).at(-1)).toBe('n');
    expect(all(context.root, 'widget-table-row').map((tableRow) => text(tableRow.children[1]))).toEqual(['50% * small base', '46.28%', '25% * small base']);
    expect(text(byTestId(context.root, 'widget-table-note'))).toBe('* Base below 30.');
    expect(byTestId(context.root, 'widget-table-note')!.closest('.paper\:hidden')).toBeNull();
  });
});
