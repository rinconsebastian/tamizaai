import { HttpTestingController } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { configureApp } from '../../../../testing/project-harness';
import { tick } from '../../../../testing/cdk';
import { ChartType, DashboardBases, IndicatorEntry, QueryResult, WidgetConfig } from '../../../core/projects/indicators.service';
import { all, cell, config, indicator, isQuery, result, row, seriesResult, text } from './indicators-testing';
import { WidgetView } from './widget-view';

// The lines under a widget and the marks of its table (add-small-sample-warnings, design D10, D11): the small-base note, the base n line and
// the no-data line sit between the chart and the footnote, and the table says where a base is small. The widget is given its dashboard's base
// settings, as the page gives them, and may set its own.

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
    [tileRows]="tileRows()"
    [(asTable)]="asTable"
  />`,
})
class Host {
  readonly type = signal<ChartType>('column');
  readonly config = signal<WidgetConfig | null>(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna' }));
  readonly entry = signal<IndicatorEntry>(teatro);
  readonly bases = signal<DashboardBases>({ minN: null, showN: false });
  readonly tileRows = signal<number | null>(null);
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

      expect(text(line('widget-n-line'))).toBe('n = 223 Hogares');
    });

    it('names the declared base column in place of the denominator', async () => {
      const { line } = await render(withN(476), (host) => {
        host.bases.set({ minN: null, showN: true });
        host.entry.set(indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', columns: { denominator: { label: 'Hogares' }, hogares: { label: 'Hogares encuestados', base: true } } }));
      });

      expect(text(line('widget-n-line'))).toBe('n = 476 Hogares encuestados');
    });

    it('is hidden by a widget that hides it, and the others still show it (Hidden by a widget)', async () => {
      const { line } = await render(withN(223), (host) => {
        host.bases.set({ minN: null, showN: true });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { showN: false } }));
      });

      expect(line('widget-n-line')).toBeNull();
    });

    it('is shown by a widget that shows it, whatever the dashboard says (Shown by a widget)', async () => {
      const { line } = await render(withN(223), (host) => host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { showN: true } })));

      expect(text(line('widget-n-line'))).toBe('n = 223 Hogares');
    });

    it('shows no line while no setting asks for it (Widgets without base settings)', async () => {
      const { root } = await render(withN(223));

      expect(root.querySelector('[data-testid="widget-n-line"]')).toBeNull();
      expect(root.querySelector('[data-testid="widget-small-note"]')).toBeNull();
      expect(root.querySelector('[data-testid="widget-no-data-line"]')).toBeNull();
    });

    it('shows no line for a widget that has no n, and says nothing (Widget without a base n)', async () => {
      const { line } = await render(withN(null), (host) => host.bases.set({ minN: null, showN: true }));

      expect(line('widget-n-line')).toBeNull();
    });

    it('writes the n with thousands separators', async () => {
      const { line } = await render(withN(12450), (host) => host.bases.set({ minN: null, showN: true }));

      expect(text(line('widget-n-line'))).toBe('n = 12,450 Hogares');
    });
  });

  describe('small-base note', () => {
    it('says under the chart that marks with a base below the threshold are dimmed (Columns dimmed)', async () => {
      const { line } = await render(withN(223), (host) => host.bases.set({ minN: 30, showN: false }));

      expect(text(line('widget-small-note'))).toBe('Marks with a base below 30 are dimmed.');
    });

    it('says nothing when no mark is dimmed (Nothing dimmed)', async () => {
      const { line } = await render(withN(223), (host) => host.bases.set({ minN: 10, showN: false }));

      expect(line('widget-small-note')).toBeNull();
    });

    it('follows the threshold of the widget over the dashboard\'s, and says nothing when the widget turns it off', async () => {
      const own = await render(withN(223), (host) => {
        host.bases.set({ minN: 30, showN: false });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { minN: 17 } }));
      });
      expect(text(own.line('widget-small-note'))).toBe('Marks with a base below 17 are dimmed.');

      TestBed.resetTestingModule();
      const off = await render(withN(223), (host) => {
        host.bases.set({ minN: 30, showN: false });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { minN: 0 } }));
      });
      expect(off.line('widget-small-note')).toBeNull();
    });

    it('is not drawn under a table, which says it with its own note', async () => {
      const { line, host, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: 30, showN: false }));
      expect(line('widget-small-note')).not.toBeNull();

      host.asTable.set(true);
      await settle();

      expect(line('widget-small-note')).toBeNull();
      expect(text(line('widget-table-note'))).toBe('* Base below 30.');
    });

    it('redraws when the threshold changes, without asking the server again (Threshold previewed)', async () => {
      const { line, host, http, settle } = await render(withN(223), (setup) => setup.bases.set({ minN: 30, showN: false }));

      host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { minN: 100 } }));
      await settle();

      expect(http.match(isQuery).filter((request) => !request.cancelled)).toHaveLength(0);
      expect(text(line('widget-small-note'))).toBe('Marks with a base below 100 are dimmed.');
    });
  });

  describe('no-data line', () => {
    const percentOfCounts = (noData: QueryResult['noData']) => withN(223, { noData });

    it('says what the left-out rows held in the indicator\'s format, whether or not the n line shows (Rate of the left-out rows)', async () => {
      const { line } = await render(percentOfCounts({ values: ['No sabe'], value: 5 / 223, numerator: 5, denominator: 223, n: 223 }));

      expect(text(line('widget-no-data-line'))).toBe('No sabe: 2.24%');
    });

    it('adds their n while the base n line shows', async () => {
      const { line } = await render(percentOfCounts({ values: ['No sabe'], value: 5 / 223, n: 223 }), (host) => host.bases.set({ minN: null, showN: true }));

      expect(text(line('widget-no-data-line'))).toBe('No sabe: 2.24% · n = 223');
    });

    it('says for a count its value with the unit of the widget and its share of the total (Share of a count)', async () => {
      const { line } = await render(withN(476, { noData: { values: ['4. No sabe'], value: 11, share: 11 / 476 } }), (host) => {
        host.entry.set(indicator('teatro_conteo', { label: 'Teatro conteo', format: 'count', unit: 'hogares' }));
        host.config.set(config({ indicator: 'teatro_conteo', measure: 'value', groupBy: 'comuna', missingFooter: true }));
      });

      expect(text(line('widget-no-data-line'))).toBe('4. No sabe: 11 hogares (2.3%)');
    });

    it('writes nothing when no row was left out (Nothing left out)', async () => {
      const { line } = await render(withN(223));

      expect(line('widget-no-data-line')).toBeNull();
    });
  });

  describe('place', () => {
    it('sits after the chart and before the footnote, in the order small note, n line, no-data line (Lines above the footnote)', async () => {
      const { root, line } = await render(withN(223, { noData: { values: ['No sabe'], value: 0.02 } }), (host) => {
        host.bases.set({ minN: 30, showN: true });
        host.config.set(config({ indicator: 'teatro_hoy', ratio, groupBy: 'comuna', display: { footnote: 'No suman 100 %: respuesta múltiple.' } }));
      });

      const order = ['chart-box', 'widget-small-note', 'widget-n-line', 'widget-no-data-line', 'widget-footnote'].map((id) => root.querySelector(`[data-testid="${id}"]`)!);
      expect(order.every(Boolean)).toBe(true);
      order.slice(1).forEach((node, i) => expect(order[i].compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING, order[i + 1].getAttribute('data-testid')!).toBeTruthy());
      expect(text(line('widget-footnote'))).toBe('No suman 100 %: respuesta múltiple.');
    });

    it('cuts the chart at its own box while a line shows, and not otherwise (Chart cut above the lines)', async () => {
      const shown = await render(withN(223), (host) => host.bases.set({ minN: null, showN: true }));
      const box = shown.root.querySelector('app-axis-chart')!.parentElement!;
      expect(box.classList).toContain('overflow-hidden');

      TestBed.resetTestingModule();
      const none = await render(withN(223));
      expect(none.root.querySelector('app-axis-chart')!.parentElement!.classList).not.toContain('overflow-hidden');
    });

    it('clamps a line to two lines, or to one on a tile two rows high or less, with its whole text in its title, and prints it whole', async () => {
      const long = 'Un nombre de columna muy largo para la base de hogares que asistieron al evento';
      const { line, host, settle } = await render(withN(223), (setup) => {
        setup.bases.set({ minN: null, showN: true });
        setup.entry.set(indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', columns: { denominator: { label: long } } }));
      });

      const n = line('widget-n-line')!;
      expect(n.classList).toContain('line-clamp-2');
      expect(n.classList).toContain('paper:line-clamp-none');
      expect(n.getAttribute('title')).toBe(`n = 223 ${long}`);
      expect(text(n)).toBe(`n = 223 ${long}`);

      host.tileRows.set(2);
      await settle();
      expect(line('widget-n-line')!.classList).toContain('line-clamp-1');
      expect(line('widget-n-line')!.classList).not.toContain('line-clamp-2');
    });

    it('hands the lines to an image in the same order, the small-base note first', async () => {
      const { view } = await render(withN(223, { noData: { values: ['No sabe'], value: 0.02 } }), (host) => host.bases.set({ minN: 30, showN: true }));

      expect((view as unknown as { exportNotesBelow(): string[] }).exportNotesBelow()).toEqual(['Marks with a base below 30 are dimmed.', 'n = 223 Hogares', 'No sabe: 2%']);
    });

    it('hands an image no note while no setting is on', async () => {
      const { view } = await render(withN(223));

      expect((view as unknown as { exportNotesBelow(): string[] }).exportNotesBelow()).toEqual([]);
    });

    it('shows the lines under the message of a widget whose rows were all left out', async () => {
      const { line, root } = await render(result([], { noData: { values: ['Sin dato'], value: 1 } }));

      expect(root.querySelector('[data-testid="widget-empty"]')).not.toBeNull();
      expect(text(line('widget-no-data-line'))).toBe('Sin dato: 100%');
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
