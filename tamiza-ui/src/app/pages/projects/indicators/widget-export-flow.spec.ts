import { TestBed } from '@angular/core/testing';
import { settle } from '../../../../testing/project-harness';
import { ToastService } from '../../../shared/ui';
import { DashboardFilter } from '../../../core/projects/indicators.service';
import { savedConfig } from './widget-view';
import { Context, all, clearOverlay, config, dashboardUrl, downloader, indicator, open, result, row, text, wait, widget } from './indicators-testing';

const rows = result([row('Cali', 75), row('Pasto', 40)]);
const coverage = widget('w1', { title: 'Coverage by municipality', layout: { x: 0, y: 0, w: 6, h: 4 } });
const exportsUrl = `${dashboardUrl}/widget-exports`;
const isExport = (r: { url: string; method: string }) => r.method === 'POST' && r.url === exportsUrl;
const answer = { downloadUrl: '/api/v1/data-exports/t0k3n', fileName: 'x.xlsx', rowCount: 32, publishedAt: '2026-10-02T12:00:00Z', expiresAt: '2026-10-02T12:05:00Z' };
const toasts = () => TestBed.inject(ToastService).toasts().map((toast) => String(toast.message));

async function choose(context: Context, format: 'png' | 'svg' | 'xlsx', index = 0) {
  all(context.root, 'widget-menu')[index].click();
  await settle(context.harness);
  document.querySelector<HTMLElement>(`[data-testid="widget-export-${format}"]`)!.click();
  await settle(context.harness);
}

// Making a widget's files from the dashboard page (add-widget-export: Widget workbook, Feedback while a widget export runs).
describe('exporting a widget from the dashboard', () => {
  beforeEach(() => {
    downloader.start.mockClear();
    downloader.save.mockClear();
  });
  afterEach(() => {
    clearOverlay();
  });

  describe('the workbook', () => {
    it('sends the widget\'s saved config, the interface\'s locale and no version, downloads the link and ends with one message', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      const request = context.http.expectOne(isExport);

      expect(request.request.body).toEqual({
        format: 'xlsx',
        title: 'Coverage by municipality',
        chartType: 'column',
        config: savedConfig('column', coverage.config),
        filters: [],
        locale: expect.stringMatching(/^[a-z]{2}(-[A-Z]{2})?$/),
      });
      expect('version' in request.request.body).toBe(false);
      request.flush(answer);
      await settle(context.harness);

      expect(downloader.start).toHaveBeenCalledWith('/api/v1/data-exports/t0k3n');
      expect(toasts()).toEqual(['The workbook of “Coverage by municipality” is ready: 32 rows. The download starts now.']);
    });

    it('sends the widget as it is saved: its display with the labels, the color and the texts (Own label in the workbook, Widget context in the workbook)', async () => {
      const display = {
        color: 3,
        labels: { municipality: 'Comuna', numerator: ' ' },
        subtitle: '¿Dónde ha visto teatro?',
        footnote: 'No suman 100 %.',
        caption: { text: '% de quienes asistieron' },
        unit: 'de quienes asistieron',
      };
      const labelled = widget('w1', { title: 'Coverage by municipality', config: config({ display }) });
      const context = await open('viewer', [labelled], rows);

      await choose(context, 'xlsx');
      const request = context.http.expectOne(isExport);

      expect(request.request.body.config.display).toEqual({ ...display, labels: { municipality: 'Comuna' } });
      expect(request.request.body.config).toMatchObject({ indicator: 'coverage', groupBy: 'municipality' });
      request.flush(answer);
      await settle(context.harness);
    });

    it('sends a map with its tooltip labels, formats and basemap (Map tooltip labels in the workbook)', async () => {
      const tooltip = [
        { column: 'registros', label: 'Total propio', format: 'count' as const },
        { column: 'calles', label: null, format: null },
      ];
      const map = widget('w1', {
        title: 'Coverage map',
        chartType: 'map',
        config: config({ geometryColumn: 'geom', groupBy: 'municipality', tooltip, basemap: 'satellite' }),
      });
      const context = await open('viewer', [map], rows);

      await choose(context, 'xlsx');
      const request = context.http.expectOne(isExport);

      expect(request.request.body.chartType).toBe('map');
      expect(request.request.body.config).toMatchObject({ geometryColumn: 'geom', basemap: 'satellite' });
      expect(request.request.body.config.tooltip).toEqual(tooltip);
      request.flush(answer);
      await settle(context.harness);
    });

    it('sends a request without a display for a widget that sets nothing the kind takes, as it always did', async () => {
      const ramped = widget('w1', { title: 'Coverage by municipality', config: config({ display: { ramp: 'green' } }) });
      const context = await open('viewer', [ramped], rows);

      await choose(context, 'xlsx');
      const request = context.http.expectOne(isExport);

      expect('display' in request.request.body.config).toBe(false);
      request.flush(answer);
      await settle(context.harness);
    });

    it('says only that widget is busy until the file is ready', async () => {
      const second = widget('w2', { title: 'Second', layout: { x: 6, y: 0, w: 6, h: 4 } });
      const context = await open('viewer', [coverage, second], rows);

      await choose(context, 'xlsx');
      const [busy, idle] = all(context.root, 'widget-menu');
      expect(busy.getAttribute('aria-busy')).toBe('true');
      expect(idle.getAttribute('aria-busy')).toBeNull();

      context.http.expectOne(isExport).flush(answer);
      await settle(context.harness);
      expect(all(context.root, 'widget-menu')[0].getAttribute('aria-busy')).toBeNull();
    });

    it('says "1 row" for one row', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ ...answer, rowCount: 1 });
      await settle(context.harness);

      expect(toasts()).toEqual(['The workbook of “Coverage by municipality” is ready: 1 row. The download starts now.']);
    });

    it('sends the dashboard filters in force, marking the ones the widget ignores', async () => {
      const filters: DashboardFilter[] = [
        { column: 'municipality', label: 'Municipality', kind: 'text', default: { values: ['Cali'] } },
        { column: 'round', label: 'Round', kind: 'number', default: { min: '3' } },
      ];
      const ignoring = widget('w2', { title: 'Ignoring', layout: { x: 6, y: 0, w: 6, h: 4 }, config: config({ ignoredDashboardFilters: ['round'] }) });
      const context = await open('viewer', [coverage, ignoring], rows, {}, { filters });

      await choose(context, 'xlsx', 1);

      expect(context.http.expectOne(isExport).request.body.filters).toEqual([
        { column: 'municipality', kind: 'text', values: ['Cali'], ignored: false },
        { column: 'round', kind: 'number', min: '3', ignored: true },
      ]);
    });

    it('adds that the file holds data published after the page was loaded', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ ...answer, publishedAt: '2026-10-02T15:41:00Z' });
      await settle(context.harness);

      expect(toasts()[0]).toMatch(/is ready: 32 rows\. The download starts now\. It holds data published .+, after this page was loaded\. Reload the page to see it\.$/);
    });

    it('tells the member to reload when the dashboard\'s filters changed, and opens again', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ code: 'widgetExports.filtersChanged', title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
      await settle(context.harness);

      expect(downloader.start).not.toHaveBeenCalled();
      expect(toasts()).toEqual(['The dashboard’s filters changed since the page was opened. Reload the dashboard and try again.']);
      expect(all(context.root, 'widget-menu')[0].getAttribute('aria-busy')).toBeNull();
    });

    it('shows the API\'s own words when a column is gone', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'xlsx');
      context.http.expectOne(isExport).flush({ code: 'widgetExports.missingColumns', detail: 'The published table no longer has the columns: municipality.' }, { status: 422, statusText: 'Unprocessable' });
      await settle(context.harness);

      expect(toasts()).toEqual(['The published table no longer has the columns: municipality.']);
    });
  });

  describe('an image', () => {
    it('saves an SVG under the name of the project, the dashboard, the widget and the day, and says so once', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'svg');
      await wait(300);
      await settle(context.harness);

      expect(downloader.save).toHaveBeenCalledTimes(1);
      const [blob, name] = downloader.save.mock.calls[0] as [Blob, string];
      expect(name).toMatch(/^[a-z0-9-]+_indicators_coverage-by-municipality_\d{4}-\d{2}-\d{2}\.svg$/);
      expect(blob.type).toBe('image/svg+xml;charset=utf-8');
      const svg = await blob.text();
      expect(svg).toContain('>Coverage by municipality<');
      expect(svg).not.toMatch(/data-testid|class=|<script|href=/);
      expect(toasts()).toEqual(['“Coverage by municipality” was saved as an SVG image.']);
      expect(context.http.match((r) => r.method === 'POST' && r.url.endsWith('/widget-exports'))).toHaveLength(0);
    });

    /** The SVG text the page saved for the first widget. */
    async function savedSvg(context: Context): Promise<string> {
      await choose(context, 'svg');
      await wait(300);
      await settle(context.harness);
      expect(downloader.save).toHaveBeenCalledTimes(1);
      return (downloader.save.mock.calls[0] as [Blob, string])[0].text();
    }

    it('writes the whole subtitle under the title and the whole footnote before the footer, and the dashboard\'s source in the footer (Texts in an image)', async () => {
      const subtitle = '¿En qué lugares ha visto teatro en el último año? (E4), preguntado a quienes asistieron a alguna función en las zonas visitadas';
      const footnote = 'No suman 100 %: respuesta múltiple. '.repeat(8).trim();
      const texted = widget('w1', { title: 'Coverage by municipality', config: config({ display: { subtitle, footnote } }) });
      const context = await open('viewer', [texted], rows, {}, { source: 'Encuesta de mercado de la zona, 478 encuestas' });

      const svg = await savedSvg(context);
      const lines = Array.from(svg.matchAll(/<text [^>]*font-size="(\d+)"[^>]*>([^<]*)</g)).map((m) => ({ size: Number(m[1]), text: m[2] }));
      const joined = (from: number, size: number) => lines.filter((l, i) => i >= from && l.size === size).map((l) => l.text).join(' ');

      expect(svg).toContain('>Coverage by municipality<');
      expect(lines.filter((l) => l.size === 12).map((l) => l.text).join(' ')).toContain('¿En qué lugares ha visto teatro');
      expect(joined(0, 12)).toContain('No suman 100 %: respuesta múltiple.');
      expect(joined(0, 12).split('No suman 100 %').length - 1).toBe(8); // whole, not cut
      expect(svg).not.toContain('…');
      const source = lines.findIndex((l) => l.text === 'Source: Encuesta de mercado de la zona, 478 encuestas');
      const published = lines.findIndex((l) => l.text.startsWith('Data published'));
      expect(source).toBeGreaterThan(0);
      expect(lines[source].size).toBe(11); // in the footer, after the project and dashboard line and before the publication line
      expect(source).toBeLessThan(published);
      expect(lines[source - 1].text).toMatch(/ · Indicators$/);
      // The subtitle comes before the caption's place and the footnote after the chart, in the order of the design.
      expect(svg.indexOf('¿En qué lugares')).toBeLessThan(svg.indexOf('No suman 100 %'));
      expect(svg.indexOf('No suman 100 %')).toBeLessThan(svg.indexOf('Source: Encuesta'));
    });

    it('names what is plotted with the widget\'s own caption text in the image (Own caption text)', async () => {
      const own = widget('w1', { title: 'Coverage by municipality', config: config({ display: { caption: { text: '% de quienes asistieron' } } }) });
      const context = await open('viewer', [own], rows);

      const svg = await savedSvg(context);

      expect(svg).toContain('>% de quienes asistieron by municipality<');
    });

    it('leaves the caption line out of the image of a widget that hides it, and keeps the title (Caption hidden)', async () => {
      const hidden = widget('w1', { title: 'Theatre attendance', config: config({ display: { caption: { hidden: true } } }) });
      const context = await open('viewer', [hidden], rows);
      expect(text(all(context.root, 'chart-caption')[0])).toBe('Coverage by municipality'); // still on the page, for screen readers

      const svg = await savedSvg(context);

      expect(svg).toContain('>Theatre attendance<');
      expect(svg).not.toContain('Coverage by municipality');
    });

    it('draws no caption line above the chart of a widget whose subtitle took it out of sight, and the own text whole as the first line of its foot (Caption hidden, Own caption text)', async () => {
      const subtitle = 'E1, % de los hogares de cada estrato que asistieron en el último año';
      const own = widget('w1', { title: 'Coverage by municipality', config: config({ display: { subtitle, caption: { text: '% de los hogares que asistieron' }, footnote: 'No suman 100 %.' } }) });
      const context = await open('viewer', [own], rows);
      expect(all(context.root, 'chart-caption')).toHaveLength(0);

      const svg = await savedSvg(context);

      const line = '>% de los hogares que asistieron by municipality<';
      expect(svg.split(line).length - 1).toBe(1);
      // The chart's own texts end with its ticks; the line comes after them, before the footnote, which comes before the footer.
      expect(svg.indexOf(line)).toBeGreaterThan(svg.indexOf('>Cali<'));
      expect(svg.indexOf(line)).toBeLessThan(svg.indexOf('No suman 100 %.'));
      expect(svg.indexOf('No suman 100 %.')).toBeLessThan(svg.indexOf('Data published'));
      expect(svg).not.toMatch(/data-testid|class=|<script|href=/);
    });

    it('keeps the caption line above the chart of a widget whose caption is kept in sight under its subtitle, and none in the foot', async () => {
      const kept = widget('w1', { title: 'Coverage by municipality', config: config({ display: { subtitle: 'Pregunta', caption: { text: '% de quienes asistieron', hidden: false } } }) });
      const context = await open('viewer', [kept], rows);

      const svg = await savedSvg(context);

      expect(svg.split('>% de quienes asistieron by municipality<').length - 1).toBe(1);
      expect(svg.indexOf('>% de quienes asistieron by municipality<')).toBeLessThan(svg.indexOf('>Cali<'));
    });

    it('writes the texts the foot opens as lines under the chart, whole, and no chip or panel (Dimmed columns in an image, Chip open)', async () => {
      const dim = result([row('COMUNA 7', 0.5, null, { numerator: 8, denominator: 16, n: 16 }), row('COMUNA 1', 0.46, null, { numerator: 56, denominator: 121, n: 121 }), row('SIN NOMBRE', 0.25, null, { numerator: 5, denominator: 20, n: 20 })], {
        n: 157,
        noData: { values: ['No sabe'], value: 0.0224 },
      });
      const labelled = widget('w1', { title: 'Coverage by municipality', config: config({ indicator: 'teatro_hoy', ratio: { numerator: 'numerator', denominator: 'denominator' }, groupBy: 'comuna', display: { footnote: 'No suman 100 %.' } }) });
      const context = await open('viewer', [labelled], dim, { indicators: [indicator('teatro_hoy', { label: 'Teatro hoy', format: 'percent', columns: { denominator: { label: 'Hogares' } } })] }, { bases: { minN: 30, showN: true } });
      // A chip is open when the member exports: the image holds its text as a line and no panel.
      (context.root.querySelector('[data-testid="foot-chip-small"]') as HTMLButtonElement).click();
      await settle(context.harness);
      expect(document.querySelector('[data-testid="foot-panel"]')).not.toBeNull();

      const svg = await savedSvg(context);

      const small = 'Small base (below 30): COMUNA 7 (n = 16), SIN NOMBRE (n = 20). Their marks are drawn dimmed.';
      // A long line wraps over several texts of the picture, so the lines are read as one run of its texts.
      const flat = Array.from(svg.matchAll(/<text [^>]*>([^<]*)</g)).map((m) => m[1]).join(' ');
      const order = ['n = 157 Hogares', small, 'No sabe: 2.24%', 'No suman 100 %.'].map((text) => flat.indexOf(text));
      expect(order.every((at) => at > 0)).toBe(true);
      expect([...order].sort((a, b) => a - b)).toEqual(order);
      expect(svg).not.toMatch(/foot-chip|foot-panel|aria-expanded|<button/);
      expect(svg).not.toContain('2 with a small base');
    });

    it('draws a stat tile at full contrast with its line without filters, then the n as the first line of its foot, the footnote and the footer (Secondary lines in the image, Small base in the image)', async () => {
      const tile = widget('w1', {
        title: 'Mediana de lo que pagan hoy al mes',
        chartType: 'stat',
        layout: { x: 0, y: 0, w: 3, h: 3 },
        config: config({ indicator: 'teatro_precio_hogar', measure: 'value', groupBy: null, statistic: 'median', display: { minN: 30, secondary: ['n', 'unfiltered'], footnote: 'Sin ponderar.' } }),
      });
      const context = await open('viewer', [tile], result([row(null, 60000, null, { n: 26 })], { n: 26, unfiltered: 65000 }), { indicators: [indicator('teatro_precio_hogar', { label: 'Teatro precio hogar', format: 'count', unit: 'pesos' })] });

      const svg = await savedSvg(context);

      const flat = Array.from(svg.matchAll(/<text [^>]*>([^<]*)</g)).map((m) => m[1]).join(' ');
      const figure = /<text [^>]*>60,000 pesos</.exec(svg)![0];
      expect(figure).not.toContain('fill-opacity');
      const order = ['60,000 pesos', 'Median of', 'Without filters: 65,000 pesos', 'Small base: the figure rests on n = 26, below the 30 this dashboard asks for. Read it with caution.', 'Sin ponderar.', 'Data published'].map((text) => flat.indexOf(text));
      expect(order.every((at) => at >= 0)).toBe(true);
      expect([...order].sort((a, b) => a - b)).toEqual(order);
      expect(svg).not.toContain('Marks with a base below');
    });

    it('holds no description of the indicator in the image, though the widget shows its button (Not printed or exported)', async () => {
      const described = [indicator('coverage', { description: 'Share of households that visited a theatre in the last year.' })];
      const shown = widget('w1', { title: 'Coverage by municipality', config: config({ display: { showDescription: true } }) });
      const context = await open('viewer', [shown], rows, { indicators: described });
      expect(all(context.root, 'widget-description')).toHaveLength(1);

      const svg = await savedSvg(context);

      expect(svg).not.toContain('Share of households');
    });

    it('draws no line of a source and no text of the widget for a widget and a dashboard without them', async () => {
      const context = await open('viewer', [coverage], rows);

      const svg = await savedSvg(context);

      expect(svg).not.toContain('Source:');
      expect(svg.match(/font-size="11"/g)).toHaveLength(2); // the footer's publication line and the line of the project, dashboard and day
    });

    // The chart annotations (add-chart-annotations, Chart annotations in widget images).
    it('draws each reference line with its text on its box in the plot, above the footnote, which is above the footer (Mean line in an image, Lines above the footnote)', async () => {
      const lined = widget('w1', {
        title: 'Coverage by municipality',
        config: config({ display: { footnote: 'No suman 100 %.', referenceLines: [{ kind: 'mean' }, { kind: 'value', value: 50, label: 'Meta' }] } }),
      });
      const context = await open('viewer', [lined], rows);

      const svg = await savedSvg(context);

      expect(svg).toContain('>Mean: 58<');
      expect(svg).toContain('>Meta: 50<');
      // Each text is drawn on its box (a rounded rect 16 px high) in the plot (jsdom has no style sheet, so the dash of the lines and the
      // color of the box are checked in the stack verification); no legend entry repeats the texts (improve-widget-text-hierarchy, design D10).
      expect(svg.match(/<rect [^>]*height="16"[^>]*rx="3"/g)).toHaveLength(2);
      expect(svg.match(/>Mean: 58</g)).toHaveLength(1);
      expect(svg.match(/>Meta: 50</g)).toHaveLength(1);
      expect(svg.indexOf('>Meta: 50<')).toBeLessThan(svg.indexOf('No suman 100 %.'));
      expect(svg.indexOf('No suman 100 %.')).toBeLessThan(svg.indexOf('Data published'));
      expect(svg).not.toMatch(/data-testid|class=|<script|href=/);
    });

    it('draws the reference lines of the plot, their labels and the data labels in the image, and no legend entry for the line (Data labels in an image)', async () => {
      const labelled = widget('w1', { title: 'Coverage by municipality', config: config({ display: { dataLabels: true, referenceLines: [{ kind: 'mean', label: 'Promedio' }] } }) });
      const context = await open('viewer', [labelled], rows);

      const svg = await savedSvg(context);
      const texts = Array.from(svg.matchAll(/<text [^>]*>([^<]*)</g)).map((m) => m[1]);

      // The plot's own texts (jsdom has no style sheet, so the halo's stroke is checked in svg-clean.spec.ts).
      expect(texts.filter((t) => t === '75')).toHaveLength(1); // Cali's label; 75 is no tick of the axis
      expect(texts.filter((t) => t === '40')).toHaveLength(2); // Pasto's label and the axis tick
      expect(texts.filter((t) => t === 'Promedio: 58')).toHaveLength(1);
    });

    it('leaves out of the image the data labels the chart left out: bars thinner than their text (Data labels in an image)', async () => {
      const thin = widget('w1', { title: 'Coverage by municipality', chartType: 'bar', config: config({ display: { dataLabels: true } }) });
      const many = result(Array.from({ length: 40 }, (_, i) => row(`Municipio ${i + 1}`, 101 + i * 2)));
      const context = await open('viewer', [thin], many);

      const svg = await savedSvg(context);
      const texts = Array.from(svg.matchAll(/<text [^>]*>([^<]*)</g)).map((m) => m[1]);

      expect(texts).toContain('Municipio 1');
      expect(texts).not.toContain('101');
      expect(texts).not.toContain('179');
    });

    it('draws no line, key or label for a widget that sets none, as before', async () => {
      const context = await open('viewer', [coverage], rows);

      const svg = await savedSvg(context);

      expect(svg).not.toContain('stroke-dasharray');
      expect(svg).not.toContain('paint-order');
    });

    it('sends no request to make an image, and makes no version', async () => {
      const context = await open('analyst', [coverage], rows);

      await choose(context, 'svg');
      await wait(300);
      await settle(context.harness);

      expect(context.http.match((r) => r.method !== 'GET')).toHaveLength(0);
    });

    it('says the PNG could not be made, and suggests the SVG, where the browser cannot draw it', async () => {
      const context = await open('viewer', [coverage], rows);

      await choose(context, 'png');
      await wait(300);
      await settle(context.harness);

      expect(downloader.save).not.toHaveBeenCalled();
      expect(toasts()).toEqual(['The PNG image could not be made. Try the SVG image.']);
      expect(all(context.root, 'widget-menu')[0].getAttribute('aria-busy')).toBeNull();
    });
  });
});

