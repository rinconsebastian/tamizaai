import { ExportParts, ExportRegistration } from '../export-parts';
import { ImageEnvironment, ImageJob, makeImage } from './widget-exporter';
import { RasterDeps } from './rasterize';
import { StyleReader } from './svg-clean';

const styleOf: StyleReader = (element) => {
  const own: Record<string, string> = { fill: 'rgb(0, 0, 0)', stroke: 'none', 'stroke-width': '1px', 'font-size': '11px' };
  const inline = (element as SVGElement).style;
  for (const property of ['fill', 'stroke']) {
    const value = inline?.getPropertyValue(property);
    if (value) own[property] = value;
  }
  return (property) => own[property] ?? '';
};

function environment(): ImageEnvironment & { disposed: number; probed: Element[] } {
  const state = { disposed: 0, probed: [] as Element[] };
  return {
    theme: { heading: '#111', body: '#444', border: '#ddd', shade: '#f4f4f4', surface: '#fff', strong: '#999', warning: '#a60' },
    fontFamily: 'system-ui, sans-serif',
    resolve: (css) => (css.startsWith('var(--tmz-seq-') ? '#123456' : css.startsWith('var(--tmz-pal-') ? 'rgb(10, 20, 30)' : css),
    measure: (text, size) => text.length * 6.5 * (size / 12),
    clean: { styleOf, prefix: '' },
    probe: (source, work) => {
      state.probed.push(source);
      return work(source.cloneNode(true) as Element);
    },
    dispose: () => state.disposed++,
    get disposed() { return state.disposed; },
    get probed() { return state.probed; },
  };
}

const liveChart = () => {
  const host = document.createElement('div');
  host.innerHTML = `<svg width="368" height="200" viewBox="0 0 368 200" class="tmz-chart-svg" data-testid="chart-svg" role="img" aria-label="x">
      <rect class="hover:opacity-80" x="10" y="20" width="30" height="100" style="fill: var(--tmz-pal-tamiza-1)" data-testid="chart-mark"><title>Cali: 75</title></rect>
      <rect fill="transparent" x="0" y="0" width="368" height="200" data-testid="chart-hit" />
      <text x="10" y="190" font-size="11">Cali</text>
    </svg>`;
  return host.firstElementChild as SVGElement;
};

function job(overrides: Partial<ImageJob> = {}, parts: Partial<ExportParts> = {}, plot: SVGElement | null = liveChart()) {
  const calls: string[] = [];
  const registration: ExportRegistration = {
    settle: () => calls.push('settle'),
    plot: () => plot,
    parts: () => {
      calls.push('parts');
      return { caption: 'Coverage: value', legend: null, notes: [], stat: null, table: null, ...parts };
    },
  };
  const value: ImageJob = {
    format: 'svg',
    title: 'Coverage by municipality',
    subtitle: null,
    footnote: null,
    captionHidden: false,
    bodyWidth: 368,
    registration,
    notesAbove: [],
    notesBelow: [],
    mapPicture: null,
    basemapNote: null,
    facts: { project: 'Census 2026', dashboard: 'Housing', version: null, source: null, filters: ['Municipality: Cali', 'Round: All'], published: 'Data published Nov 2, 2026, 5:12 AM', exported: 'Exported Nov 2, 2026, 10:30 AM (America/Bogota)' },
    tableLabels: { rowsNotShown: (n) => `${n} rows not shown in the image.`, columnsNotShown: (n) => `${n} columns not shown in the image.`, inWorkbook: 'The Excel workbook has them all.' },
    closeTooltips: () => calls.push('closeTooltips'),
    ...overrides,
  };
  return { value, calls };
}

const read = (blob: Blob) => blob.text();

describe('makeImage', () => {
  it('makes a standalone SVG: the title and footer, the chart\'s marks with their computed colors, nothing of the page', async () => {
    const env = environment();
    const { value } = job();

    const result = await makeImage(value, env);

    const text = await read(result.blob);
    expect(result.blob.type).toBe('image/svg+xml;charset=utf-8');
    expect(text).toBe(result.svg);
    expect(text.startsWith('<?xml version="1.0" encoding="UTF-8"?>\n<svg')).toBe(true);
    expect(text).toContain('>Coverage by municipality<');
    expect(text).toContain('>Coverage: value<');
    expect(text).toContain('>Census 2026 · Housing<');
    expect(text).toContain('>Municipality: Cali · Round: All<');
    // The last line is long: it wraps at words, and none is lost.
    const footer = [...text.matchAll(/font-size="11"[^>]*>([^<]*)</g)].map((m) => m[1]).slice(3).join(' ');
    expect(footer).toBe('Data published Nov 2, 2026, 5:12 AM · Exported Nov 2, 2026, 10:30 AM (America/Bogota)');
    expect(text).toContain('width="30" height="100" fill="var(--tmz-pal-tamiza-1)"');
    expect(text).toContain('>Cali<');
    expect(text).not.toMatch(/chart-hit|data-testid|class=|aria-|role=|<title|<script|<foreignObject|href=/);
    expect(result.width).toBe(400);
    expect(env.disposed).toBe(1);
  });

  it('closes the tooltips and settles the widget before it reads what it shows', async () => {
    const { value, calls } = job();

    await makeImage(value, environment());

    expect(calls).toEqual(['closeTooltips', 'settle', 'parts']);
  });

  it('writes the tile\'s notes above the chart and what the picture leaves out under it', async () => {
    const { value } = job({ notesAbove: ['Not filtered by: Round'], basemapNote: 'The basemap is not included in the image.' }, { notes: ['2 rows were left out.'] });

    const text = await read((await makeImage(value, environment())).blob);

    const order = ['Coverage by municipality', 'Not filtered by: Round', 'Coverage: value', '2 rows were left out.', 'The basemap is not included in the image.', 'Census 2026 · Housing'];
    const at = order.map((line) => text.indexOf(`>${line}<`));
    expect(at.every((index) => index > 0)).toBe(true);
    expect(at).toEqual([...at].sort((a, b) => a - b));
  });

  it('draws a map from the engine\'s picture, at the scale\'s colors, with no tile', async () => {
    const mapPicture = { width: 300, height: 200, shapes: [{ step: 5, point: null, polygons: [[[{ x: 1, y: 1 }, { x: 50, y: 1 }, { x: 50, y: 50 }]]] }] };
    const { value } = job({ mapPicture }, { legend: { kind: 'scale', low: '5', high: '75', steps: ['var(--tmz-seq-1)', 'var(--tmz-seq-7)'], stepOpacity: 1 } }, null);

    const text = await read((await makeImage(value, environment())).blob);

    expect(text).toContain('fill="#123456"');
    expect(text).toContain('clipPath');
    expect(text).not.toMatch(/<image|<img|tile/);
  });

  it('makes a PNG by drawing that SVG, and gives it back with the SVG text', async () => {
    const drawn: string[] = [];
    const raster: RasterDeps = {
      load: async (url) => { drawn.push(url); return {} as CanvasImageSource; },
      canvas: () => ({ context: { drawImage: () => undefined } as unknown as CanvasRenderingContext2D, toBlob: async () => new Blob([new Uint8Array(0)]), release: () => undefined }),
    };
    const { value } = job({ format: 'png' });

    await expect(makeImage(value, environment(), raster)).rejects.toMatchObject({ reason: 'failed' });
    expect(drawn[0]).toMatch(/^data:image\/svg\+xml;charset=utf-8,/);
    expect(decodeURIComponent(drawn[0])).toContain('Coverage by municipality');
  });

  it('puts the data of a stat tile, a cross table or a widget shown as a table where the chart would be', async () => {
    const stat = await read((await makeImage(job({}, { caption: null, stat: { value: '1,234', description: 'Households' } }, null).value, environment())).blob);
    const table = await read((await makeImage(job({}, { table: { headers: ['g', 'v'], rows: [['Cali', '5']] } }, null).value, environment())).blob);

    expect(stat).toContain('font-size="36"');
    expect(stat).toContain('>1,234<');
    expect(table).toContain('>Cali<');
    expect(table).toContain('>v<');
  });

  it('leaves out the caption line and the description of a stat tile when the widget hides its caption (Caption hidden)', async () => {
    const withCaption = await read((await makeImage(job({}, { caption: 'Coverage: value' }).value, environment())).blob);
    const hidden = await read((await makeImage(job({ captionHidden: true }, { caption: 'Coverage: value' }).value, environment())).blob);
    const stat = await read((await makeImage(job({ captionHidden: true }, { caption: null, stat: { value: '1,234', description: 'Households' } }, null).value, environment())).blob);

    expect(withCaption).toContain('>Coverage: value<');
    expect(hidden).not.toContain('Coverage: value');
    expect(hidden).toContain('>Coverage by municipality<');
    expect(stat).toContain('>1,234<');
    expect(stat).not.toContain('Households');
  });

  it('draws the subtitle, the footnote and the source of the dashboard the job carries', async () => {
    const { value } = job({ subtitle: 'Where have you seen theatre?', footnote: 'Does not add up to 100 %.' });
    value.facts = { ...value.facts, source: 'Source: Market survey, 478 responses' };

    const svg = await read((await makeImage(value, environment())).blob);

    expect(svg).toContain('>Where have you seen theatre?<');
    expect(svg).toContain('>Does not add up to 100 %.<');
    expect(svg).toContain('>Source: Market survey, 478 responses<');
  });

  it('takes its probe away even when the picture fails', async () => {
    const env = environment();
    const { value } = job({ registration: { settle: () => undefined, plot: () => null, parts: () => { throw new Error('boom'); } } });

    await expect(makeImage(value, env)).rejects.toThrow('boom');
    expect(env.disposed).toBe(1);
  });
});
