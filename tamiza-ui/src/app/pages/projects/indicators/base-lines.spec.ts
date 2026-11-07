import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { configureApp } from '../../../../testing/project-harness';
import { tick } from '../../../../testing/cdk';
import { ChartType, DashboardBases, IndicatorEntry, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { all, cell, config, indicator, isQuery, result, row, seriesResult, text } from './indicators-testing';
import { WidgetView } from './widget-view';

// The foot of a widget and the marks of its table (add-small-sample-warnings, design D10, D11; improve-widget-text-hierarchy, design D4): the
// base n, the small-base and the no-data chips sit under the chart, before the footnote, and the table says where a base is small. Each chip
// has a line of its whole text in the page (`foot-line-<kind>`), which the tests read. The widget is given its dashboard's base settings, as
// the page gives them, and may set its own.

const ratio = { numerator: 'numerator', denominator: 'denominator' };
const teatro = indicator('teatro_hoy', {
  label: 'Teatro hoy',
  format: 'percent',
  columns: { numerator: { label: 'Hogares que asistieron' }, denominator: { label: 'Hogares' } },
});

@Component({
  imports: [WidgetView],
  template: `<app-widget-view
    projectId="p1"
    [chartType]="type()"
    [config]="config()"
    [indicator]="entry()"
    [bases]="bases()"
    [compactStat]="compactStat()"
    [columns]="columns()"
    [(asTable)]="asTable"
  />`,
})
class Host {
  readonly type = signal<ChartType>('column');
  readonly config = signal<WidgetConfig | null>(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna' }));
  readonly entry = signal<IndicatorEntry>(teatro);
  readonly bases = signal<DashboardBases>({ minN: null, showN: false });
  readonly compactStat = signal(false);
  readonly columns = signal(0);
  readonly asTable = signal(false);
}

const comunas = () => [
  row('COMUNA 7', 0.5, null, { numerator: 8, denominator: 16, n: 16 }),
  row('COMUNA 1', 56 / 121, null, { numerator: 56, denominator: 121, n: 121 }),
  row('SIN NOMBRE', 0.25, null, { numerator: 5, denominator: 20, n: 20 }),
];

async function render(answer: QueryResult, setup: (host: Host) => void = () => undefined) {
  const http: HttpTestingController = configureApp();
  const fixture = TestBed.createComponent(Host);
  setup(fixture.componentInstance);
  const settle = async () => {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await tick();
    }
  };
  await settle();
  const [first] = http.match(isQuery).filter((request) => !request.cancelled);
  first.flush(answer);
  await settle();
  const view = fixture.debugElement.query(By.directive(WidgetView)).componentInstance as WidgetView;
  const root = fixture.nativeElement as HTMLElement;
  const line = (id: string) => root.querySelector<HTMLElement>(`[data-testid="${id}"]`);
  return { fixture, host: fixture.componentInstance, http, settle, view, root, line };
}

const withN = (n: number | null, extra: Partial<QueryResult> = {}) => result(comunas(), { ...(n === null ? {} : { n }), ...extra });

describe('the lines under a widget (add-small-sample-warnings, design D11)', () => {
  describe('base n line', () => {
    it('reads "n = ", the n and the name of the denominator when the dashboard shows it (Line shown)', async () => {
      const { line } = await render(withN(223), (host) => host.bases.set({ minN: null, showN: true }));

      expect(text(line('foot-line-n'))).toBe('n = 223 Hogares');
    });

    it('names the declared base column in place of the denominator', async () => {
      const { line } = await render(withN(476), (host) => {
        host.bases.set({ minN: null, showN: true });
        host.entry.set(indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', columns: { denominator: { label: 'Hogares' }, hogares: { label: 'Hogares encuestados', base: true } } }));
      });

      expect(text(line('foot-line-n'))).toBe('n = 476 Hogares encuestados');
    });

    it('is hidden by a widget that hides it, and the others still show it (Hidden by a widget)', async () => {
      const { line } = await render(withN(223), (host) => {
        host.bases.set({ minN: null, showN: true });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { showN: false } }));
      });

      expect(line('foot-line-n')).toBeNull();
    });

    it('is shown by a widget that shows it, whatever the dashboard says (Shown by a widget)', async () => {
      const { line } = await render(withN(223), (host) => host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { showN: true } })));

      expect(text(line('foot-line-n'))).toBe('n = 223 Hogares');
    });

    it('shows no line while no setting asks for it (Widgets without base settings)', async () => {
      const { root } = await render(withN(223));

      expect(root.querySelector('[data-testid="foot-line-n"]')).toBeNull();
      expect(root.querySelector('[data-testid="foot-line-small"]')).toBeNull();
      expect(root.querySelector('[data-testid="foot-line-noData"]')).toBeNull();
    });

    it('shows no line for a widget that has no n, and says nothing (Widget without a base n)', async () => {
      const { line } = await render(withN(null), (host) => host.bases.set({ minN: null, showN: true }));

      expect(line('foot-line-n')).toBeNull();
    });

    it('writes the n with thousands separators', async () => {
      const { line } = await render(withN(12450), (host) => host.bases.set({ minN: null, showN: true }));

      expect(text(line('foot-line-n'))).toBe('n = 12,450 Hogares');
    });
  });

  describe('small-base note', () => {
    it('says under the chart that marks with a base below the threshold are dimmed (Columns dimmed)', async () => {
      const { line } = await render(withN(223), (host) => host.bases.set({ minN: 30, showN: false }));

      expect(text(line('foot-line-small'))).toBe('Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.');
    });

    it('says nothing when no mark is dimmed (Nothing dimmed)', async () => {
      const { line } = await render(withN(223), (host) => host.bases.set({ minN: 10, showN: false }));

      expect(line('foot-line-small')).toBeNull();
    });

    it('follows the threshold of the widget over the dashboard\'s, and says nothing when the widget turns it off', async () => {
      const own = await render(withN(223), (host) => {
        host.bases.set({ minN: 30, showN: false });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { minN: 17 } }));
      });
      expect(text(own.line('foot-line-small'))).toBe('Small base (below 17): COMUNA 7 (n = 16). Their marks are drawn dimmed.');

      TestBed.resetTestingModule();
      const off = await render(withN(223), (host) => {
        host.bases.set({ minN: 30, showN: false });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { minN: 0 } }));
      });
      expect(off.line('foot-line-small')).toBeNull();
    });

    it('is not drawn under a table, which says it with its own note', async () => {
      const { line, host, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: 30, showN: false }));
      expect(line('foot-line-small')).not.toBeNull();

      host.asTable.set(true);
      await settle();

      expect(line('foot-line-small')).toBeNull();
      expect(text(line('widget-table-note'))).toBe('* Base below 30.');
    });

    it('redraws when the threshold changes, without asking the server again (Threshold previewed)', async () => {
      const { line, host, http, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: 30, showN: false }));

      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { minN: 100 } }));
      await settle();

      expect(http.match(isQuery).filter((request) => !request.cancelled)).toHaveLength(0);
      expect(text(line('foot-line-small'))).toBe('Small base (below 100): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.');
    });
  });

  describe('no-data line', () => {
    const percentOfCounts = (noData: QueryResult['noData']) => withN(223, { noData });

    it('says what the left-out rows held in the indicator\'s format, whether or not the n line shows (Rate of the left-out rows)', async () => {
      const { line } = await render(percentOfCounts({ values: ['No sabe'], value: 5 / 223, numerator: 5, denominator: 223, n: 223 }));

      expect(text(line('foot-line-noData'))).toBe('No sabe: 2.24%');
    });

    it('adds their n while the base n line shows', async () => {
      const { line } = await render(percentOfCounts({ values: ['No sabe'], value: 5 / 223, n: 223 }), (host) => host.bases.set({ minN: null, showN: true }));

      expect(text(line('foot-line-noData'))).toBe('No sabe: 2.24% · n = 223');
    });

    it('says for a count its value with the unit of the widget and its share of the total (Share of a count)', async () => {
      const { line } = await render(withN(476, { noData: { values: ['4. No sabe'], value: 11, share: 11 / 476 } }), (host) => {
        host.entry.set(indicator('teatro_conteo', { label: 'Teatro conteo', format: 'count', unit: 'hogares' }));
        host.config.set(config({ indicator: 'teatro_conteo', measure: 'value', groupBy: 'comuna', missingFooter: true }));
      });

      expect(text(line('foot-line-noData'))).toBe('4. No sabe: 11 hogares (2.3%)');
    });

    it('writes nothing when no row was left out (Nothing left out)', async () => {
      const { line } = await render(withN(223));

      expect(line('foot-line-noData')).toBeNull();
    });
  });

  describe('place', () => {
    it('sits after the chart and before the footnote, in the order n, small base, no data (Lines above the footnote)', async () => {
      const { root, line } = await render(withN(223, { noData: { values: ['No sabe'], value: 0.02 } }), (host) => {
        host.bases.set({ minN: 30, showN: true });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { footnote: 'No suman 100 %: respuesta múltiple.' } }));
      });

      const order = ['chart-box', 'foot-line-n', 'foot-line-small', 'foot-line-noData', 'foot-line-note'].map((id) => root.querySelector(`[data-testid="${id}"]`)!);
      expect(order.every(Boolean)).toBe(true);
      order.slice(1).forEach((node, i) => expect(order[i].compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING, order[i + 1].getAttribute('data-testid')!).toBeTruthy());
      expect(text(line('foot-line-note'))).toBe('No suman 100 %: respuesta múltiple.');
    });

    it('cuts the chart at its own box while a line shows, and not otherwise (Chart cut above the lines)', async () => {
      const shown = await render(withN(223), (host) => host.bases.set({ minN: null, showN: true }));
      const box = shown.root.querySelector('app-axis-chart')!.parentElement!;
      expect(box.classList).toContain('overflow-hidden');

      TestBed.resetTestingModule();
      const none = await render(withN(223));
      expect(none.root.querySelector('app-axis-chart')!.parentElement!.classList).not.toContain('overflow-hidden');
    });

    it('cuts a long chip at its own width and keeps its whole text in the line that describes it, which prints whole', async () => {
      const long = 'Un nombre de columna muy largo para la base de hogares que asistieron al evento';
      const { line, root } = await render(withN(223), (setup) => {
        setup.bases.set({ minN: null, showN: true });
        setup.entry.set(indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', columns: { denominator: { label: long } } }));
      });

      const n = line('foot-line-n')!;
      expect(text(n)).toBe(`n = 223 ${long}`);
      expect(n.closest('ul')!.classList).toContain('paper:block');
      const chip = root.querySelector('[data-testid="foot-chip-n"]')!;
      expect(chip.querySelector('span')!.classList).toContain('truncate');
      expect(text(chip)).toBe(`n = 223 ${long}`);
      expect(chip.getAttribute('aria-describedby')).toBe(n.id);
    });

    it('hands the lines to an image in the same order, the n first', async () => {
      const { view } = await render(withN(223, { noData: { values: ['No sabe'], value: 0.02 } }), (host) => host.bases.set({ minN: 30, showN: true }));

      expect((view as unknown as { exportNotesBelow(): string[] }).exportNotesBelow()).toEqual(['n = 223 Hogares', 'Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.', 'No sabe: 2%']);
    });

    it('hands an image no note while no setting is on', async () => {
      const { view } = await render(withN(223));

      expect((view as unknown as { exportNotesBelow(): string[] }).exportNotesBelow()).toEqual([]);
    });

    it('shows the lines under the message of a widget whose rows were all left out', async () => {
      const { line, root } = await render(result([], { noData: { values: ['Sin dato'], value: 1 } }));

      expect(root.querySelector('[data-testid="widget-empty"]')).not.toBeNull();
      expect(text(line('foot-line-noData'))).toBe('Sin dato: 100%');
    });
  });
});

describe('the table of a widget with figures (add-small-sample-warnings, design D11)', () => {
  const tableOf = (root: HTMLElement) => ({
    headers: Array.from(root.querySelectorAll('[data-testid="widget-table"] th[scope="col"]')).map(text),
    rows: all(root, 'widget-table-row').map((tableRow) => Array.from(tableRow.children).map(text)),
  });

  it('gets an n column after the value, and marks the value of a small base with a note under it (Small base in a table)', async () => {
    const { root, host, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: 30, showN: false }));

    host.asTable.set(true);
    await settle();

    expect(tableOf(root).headers).toEqual(['comuna', 'Teatro hoy: Hogares que asistieron / Hogares', 'n']);
    expect(tableOf(root).rows).toEqual([
      ['COMUNA 7', '50% * small base', '16'],
      ['COMUNA 1', '46.28%', '121'],
      ['SIN NOMBRE', '25% * small base', '20'],
    ]);
    expect(text(root.querySelector('[data-testid="widget-table-note"]'))).toBe('* Base below 30.');
    expect(root.querySelectorAll('[data-testid="widget-table-small"]')).toHaveLength(2);
  });

  it('gets the n column while the n line shows, with no mark and no note when no threshold is in force', async () => {
    const { root, host, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: null, showN: true }));

    host.asTable.set(true);
    await settle();

    expect(tableOf(root).headers).toHaveLength(3);
    expect(tableOf(root).rows.map((cells) => cells[2])).toEqual(['16', '121', '20']);
    expect(root.querySelector('[data-testid="widget-table-note"]')).toBeNull();
    expect(root.querySelectorAll('[data-testid="widget-table-small"]')).toHaveLength(0);
  });

  it('is as it was with no base settings (Table without base settings)', async () => {
    const { root, host, settle } = await render(withN(223));

    host.asTable.set(true);
    await settle();

    expect(tableOf(root).headers).toHaveLength(2);
    expect(tableOf(root).rows).toEqual([
      ['COMUNA 7', '50%'],
      ['COMUNA 1', '46.28%'],
      ['SIN NOMBRE', '25%'],
    ]);
    expect(root.querySelector('[data-testid="widget-table-note"]')).toBeNull();
  });

  it('keeps the columns of a measure with no base column, whatever the settings say', async () => {
    const { root, host, settle } = await render(result([row('COMUNA 7', 5), row('COMUNA 1', 80)]), (setup) => {
      setup.bases.set({ minN: 30, showN: true });
      setup.config.set(config({ indicator: 'teatro_conteo', measure: 'value', groupBy: 'comuna' }));
      setup.entry.set(indicator('teatro_conteo', { label: 'Teatro conteo', format: 'count' }));
    });

    host.asTable.set(true);
    await settle();

    expect(tableOf(root).headers).toHaveLength(2);
    expect(tableOf(root).rows).toEqual([
      ['COMUNA 7', '5'],
      ['COMUNA 1', '80'],
    ]);
    expect(root.querySelector('[data-testid="widget-table-note"]')).toBeNull();
  });

  it('marks the cells of a cross table whose base is small, and explains the mark under it (Small base in a table)', async () => {
    const cells = [
      { ...cell('COMUNA 7', 'Sí', 0.5), numerator: 8, denominator: 16, n: 16 },
      { ...cell('COMUNA 7', 'No', 0.5), numerator: 8, denominator: 16, n: 16 },
      { ...cell('COMUNA 1', 'Sí', 0.46), numerator: 56, denominator: 121, n: 121 },
    ];
    const { root } = await render(seriesResult(cells, ['Sí', 'No']), (setup) => {
      setup.type.set('crosstab');
      setup.bases.set({ minN: 30, showN: false });
      setup.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', seriesBy: 'categoria' }));
    });

    expect(tableOf(root).rows.map((cellsOfRow) => cellsOfRow.slice(0, 3))).toEqual([
      ['COMUNA 1', '46%', '—'],
      ['COMUNA 7', '50% * small base', '50% * small base'],
      ['Total', expect.any(String), expect.any(String)],
    ]);
    expect(text(root.querySelector('[data-testid="widget-table-note"]'))).toBe('* Base below 30.');
    expect(tableOf(root).headers).toHaveLength(4);
  });

  it('puts the n column after the value of a pie, before its share, and marks a cell of a cross table', async () => {
    const pie = await render(withN(223), (setup) => {
      setup.type.set('pie');
      setup.bases.set({ minN: 30, showN: false });
    });
    pie.host.asTable.set(true);
    await pie.settle();
    expect(tableOf(pie.root).headers.slice(1).map((header) => header.split(':')[0])).toEqual(['Teatro hoy', 'n', 'Share']);
    expect(tableOf(pie.root).rows[0].slice(0, 3)).toEqual(['COMUNA 7', '50% * small base', '16']);
  });
});


describe('the foot of a widget (improve-widget-text-hierarchy, design D4, task 4.1)', () => {
  const chips = (root: HTMLElement) => Array.from(root.querySelectorAll<HTMLElement>('[data-testid="widget-foot"] > button')).map((chip) => chip.getAttribute('data-testid')!.replace('foot-chip-', ''));
  const label = (root: HTMLElement, kind: string) => text(root.querySelector(`[data-testid="foot-chip-${kind}"]`)?.querySelector('span') ?? null);
  const note = 'No suman 100 %: respuesta múltiple.';
  const subtitle = 'E1, % de los hogares de cada estrato que asistieron';
  const own = '% de los hogares que asistieron';
  const withDisplay = (display: NonNullable<WidgetConfig['display']>) => config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display });

  it('shows the n, the small bases and the note, in that order (Foot of a column chart)', async () => {
    const { root } = await render(withN(475), (host) => {
      host.bases.set({ minN: 30, showN: true });
      host.config.set(withDisplay({ footnote: note }));
    });

    expect(chips(root)).toEqual(['n', 'small', 'note']);
    expect(label(root, 'n')).toBe('n = 475 Hogares');
    expect(label(root, 'small')).toBe('2 with a small base');
    expect(label(root, 'note')).toBe('Note');
    expect(root.querySelector('[data-testid="foot-chip-small"]')!.getAttribute('data-tone')).toBe('warning');
    expect(root.querySelector('[data-testid="foot-chip-n"]')!.getAttribute('data-tone')).toBe('neutral');
  });

  it('puts the caption chip first when a subtitle takes the own text out of sight (Caption chip first)', async () => {
    const { root, line } = await render(withN(475), (host) => {
      host.bases.set({ minN: null, showN: true });
      host.config.set(withDisplay({ subtitle, caption: { text: own }, footnote: note }));
    });

    expect(chips(root)).toEqual(['caption', 'n', 'note']);
    expect(label(root, 'caption')).toBe(own);
    expect(text(line('foot-line-caption'))).toBe(`${own} by comuna`);
  });

  it('draws the caption chip of a pie too, and of a line chart', async () => {
    const { root } = await render(withN(475), (host) => {
      host.type.set('pie');
      host.config.set(withDisplay({ subtitle, caption: { text: own } }));
    });

    expect(chips(root)).toEqual(['caption']);
  });

  it('has no caption chip for the default line, a hidden caption, a kept caption or no subtitle (Default line under a subtitle, Hidden caption)', async () => {
    for (const display of [{ subtitle }, { subtitle, caption: { hidden: true } }, { subtitle, caption: { text: own, hidden: false } }, { caption: { text: own } }] satisfies NonNullable<WidgetConfig['display']>[]) {
      const { root } = await render(withN(475), (host) => host.config.set(withDisplay(display)));

      expect(chips(root), JSON.stringify(display)).toEqual([]);
      TestBed.resetTestingModule();
    }
  });

  it('has no caption chip for a heat map, a histogram or a chart with a series, whose line stays in sight', async () => {
    const heat = await render(seriesResult([cell('A', 'x', 0.5), cell('A', 'y', 0.4)], ['x', 'y']), (host) => {
      host.type.set('heatmap');
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', seriesBy: 'respuesta', display: { subtitle, caption: { text: own } } }));
    });

    expect(chips(heat.root)).toEqual([]);
  });

  it('opens the whole caption line, as the chart would have written it, in its panel (Chip opened)', async () => {
    const { root, fixture } = await render(withN(475), (host) => host.config.set(withDisplay({ subtitle, caption: { text: own } })));

    (root.querySelector('[data-testid="foot-chip-caption"]') as HTMLButtonElement).click();
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }

    expect(text(document.querySelector('[data-testid="foot-panel"]'))).toBe(`${own} by comuna`);
    document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));
  });

  it('draws no foot while there is nothing to say (Nothing to say)', async () => {
    const { root } = await render(withN(475));

    expect(root.querySelector('[data-testid="widget-foot"]')).toBeNull();
  });

  it('names the no-data value in its chip and opens the line as it is today', async () => {
    const { root, line } = await render(withN(223, { noData: { values: ['No sabe'], value: 5 / 223 } }));

    expect(chips(root)).toEqual(['noData']);
    expect(label(root, 'noData')).toBe('No data 2.24%');
    expect(text(line('foot-line-noData'))).toBe('No sabe: 2.24%');
  });

  it('names the share of a count in the no-data chip, in place of its value', async () => {
    const { root } = await render(withN(476, { noData: { values: ['4. No sabe'], value: 11, share: 11 / 476 } }));

    expect(label(root, 'noData')).toBe('No data 2.3%');
  });

  it('names at most five dimmed marks in the small-base chip and says how many more (More than five)', async () => {
    const marks = Array.from({ length: 8 }, (_, i) => row(`COMUNA ${i + 1}`, 0.5, null, { numerator: 1, denominator: 10, n: 10 + i }));
    const { root, line } = await render(result(marks, { n: 400 }), (host) => host.bases.set({ minN: 30, showN: false }));

    expect(label(root, 'small')).toBe('8 with a small base');
    expect(text(line('foot-line-small'))).toBe(
      'Small base (below 30): COMUNA 1 (n = 10), COMUNA 2 (n = 11), COMUNA 3 (n = 12), COMUNA 4 (n = 13), COMUNA 5 (n = 14) and 3 more. Their marks are drawn dimmed.',
    );
  });

  it('names a cell of a chart with a series by its group and its series (Cells of a series)', async () => {
    const figured = (group: string, series: string, n: number) => ({ ...cell(group, series, 0.5), numerator: 1, denominator: 2, n });
    const answer = seriesResult([figured('COMUNA 7', '1. Sí', 16), figured('COMUNA 7', '2. No', 400), figured('COMUNA 1', '1. Sí', 500)], ['1. Sí', '2. No']);
    const { root, line } = await render(answer, (host) => {
      host.type.set('column');
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', seriesBy: 'respuesta', layout: 'grouped' }));
      host.bases.set({ minN: 30, showN: false });
    });

    expect(label(root, 'small')).toBe('1 with a small base');
    expect(text(line('foot-line-small'))).toBe('Small base (below 30): COMUNA 7 · 1. Sí (n = 16). Their marks are drawn dimmed.');
  });

  it('has no small-base chip while the widget is shown as a table (Shown as a table)', async () => {
    const { root, host, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: 30, showN: false }));
    expect(chips(root)).toEqual(['small']);

    host.asTable.set(true);
    await settle();

    expect(chips(root)).toEqual([]);
  });

  it('keeps its foot under the table of a widget with a footnote, and under the message that no row matches', async () => {
    const { root, host, settle } = await render(withN(223), (setup) => setup.config.set(withDisplay({ footnote: note })));

    host.asTable.set(true);
    await settle();

    expect(chips(root)).toEqual(['note']);
    expect(root.querySelector('[data-testid="widget-table"]')!.compareDocumentPosition(root.querySelector('[data-testid="foot-chip-note"]')!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('keeps the foot out of a widget whose query failed (Widget that failed)', async () => {
    const { fixture, host, http, settle } = await render(withN(223), (setup) => setup.config.set(withDisplay({ footnote: note })));
    expect(fixture.nativeElement.querySelector('[data-testid="foot-chip-note"]')).not.toBeNull();

    host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'round', display: { footnote: note } }));
    await settle();
    http.match(isQuery).filter((request) => !request.cancelled).forEach((request) => request.flush({ title: 'x' }, { status: 500, statusText: 'Server Error' }));
    await settle();

    expect(fixture.nativeElement.querySelector('[data-testid="view-error"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="widget-foot"]')).toBeNull();
  });

  it('shows the subtitle of a compact stat tile as the Detail chip, after the others and before the note (Tile 4 columns wide and 2 rows high)', async () => {
    const { root, line } = await render(result([row(null, 0.2422, null, { numerator: 5, denominator: 20, n: 20 })], { n: 476 }), (host) => {
      host.type.set('stat');
      host.compactStat.set(true);
      host.bases.set({ minN: null, showN: false });
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { subtitle, footnote: note } }));
    });

    expect(chips(root)).toEqual(['detail', 'note']);
    expect(label(root, 'detail')).toBe('Detail');
    expect(text(line('foot-line-detail'))).toBe(subtitle);
  });

  it('keeps on paper the room of a figure and of the label lines, the foot being what a short tile cuts: one line for a label that fits a line of its columns, two otherwise or when the columns are not known (Tile 4 columns wide and 2 rows high)', async () => {
    const box = (root: HTMLElement) => root.querySelector('app-stat-widget')!.parentElement!;
    const render4 = (columns: number) =>
      render(result([row(null, 0.2422, null, { numerator: 5, denominator: 20, n: 476 })]), (host) => {
        host.type.set('stat');
        host.columns.set(columns);
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { subtitle } }));
      });
    const unknown = await render4(0);
    expect(box(unknown.root).classList).toContain('paper:min-h-[3.7rem]');
    TestBed.resetTestingModule();

    const wide = await render4(12);
    expect(box(wide.root).classList).toContain('paper:min-h-[2.6rem]');
    expect(box(wide.root).classList).not.toContain('paper:min-h-[3.7rem]');
    TestBed.resetTestingModule();

    const narrow = await render4(1);
    expect(box(narrow.root).classList).toContain('paper:min-h-[3.7rem]');
    TestBed.resetTestingModule();

    // It measures the text the tile draws as its label, own text and statistic included, not the indicator's label.
    const long = await render(result([row(null, 35000, null, { n: 476 })]), (host) => {
      host.type.set('stat');
      host.columns.set(4);
      host.config.set(config({ indicator: 'teatro_hoy', measure: 'value', groupBy: null, statistic: 'median', display: { caption: { text: 'Pagaría en promedio por una boleta de teatro en el municipio' } } }));
    });
    expect(box(long.root).classList).toContain('paper:min-h-[3.7rem]');
    TestBed.resetTestingModule();

    const chart = await render(withN(223));
    expect(chart.root.querySelector('app-axis-chart')!.parentElement!.classList.contains('paper:min-h-[3.7rem]')).toBe(false);
  });

  it('has no Detail chip for a tile that is not compact, or that has no subtitle', async () => {
    const stat = await render(result([row(null, 0.2422, null, { numerator: 5, denominator: 20, n: 476 })]), (host) => {
      host.type.set('stat');
      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: null, display: { subtitle } }));
    });

    expect(chips(stat.root)).toEqual([]);
  });

  it('hands an image the foot\'s texts in its order, but not the Detail chip or the note, which have their own place', async () => {
    const { view } = await render(withN(223, { noData: { values: ['No sabe'], value: 0.02 } }), (host) => {
      host.bases.set({ minN: 30, showN: true });
      host.config.set(withDisplay({ subtitle, caption: { text: own }, footnote: note }));
    });

    expect((view as unknown as { exportNotesBelow(): string[] }).exportNotesBelow()).toEqual([
      `${own} by comuna`,
      'n = 223 Hogares',
      'Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.',
      'No sabe: 2%',
    ]);
  });
});
