import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { DashboardPrint, printingClass } from './dashboard-print';
import { TableModel, WidgetTable } from './widget-table';

// A widget's table on paper (dashboard printing, "Tables on paper", design D8): the rows that fit its box are printed, and a note
// says how many were left out. jsdom lays nothing out, so the box and the rows' rectangles are stubbed.

function model(count: number): TableModel {
  return { headers: ['Municipality', 'Coverage'], rows: Array.from({ length: count }, (_, i) => [`Place ${i + 1}`, `${i}%`]) };
}

async function render(rows: number, lang: 'en' | 'es' = 'en') {
  TestBed.configureTestingModule({ imports: [translocoTesting(lang)], providers: [DashboardPrint] });
  const fixture = TestBed.createComponent(WidgetTable);
  fixture.componentRef.setInput('model', model(rows));
  await fixture.whenStable();
  const root = fixture.nativeElement as HTMLElement;
  return { fixture, root, print: TestBed.inject(DashboardPrint) };
}

/** The box ends at `bottom`; the rows end at `rowBottoms`, top to bottom. */
function layOut(root: HTMLElement, bottom: number, rowBottoms: number[]) {
  vi.spyOn(root, 'getBoundingClientRect').mockReturnValue({ bottom } as DOMRect);
  root.querySelectorAll<HTMLElement>('tbody > tr').forEach((row, i) => vi.spyOn(row, 'getBoundingClientRect').mockReturnValue({ bottom: rowBottoms[i] } as DOMRect));
}

/** Rows 24 px high under a 40 px header. */
const evenRows = (count: number) => Array.from({ length: count }, (_, i) => 40 + (i + 1) * 24);

const printed = (root: HTMLElement) => Array.from(root.querySelectorAll('tbody > tr')).filter((row) => !row.classList.contains('paper:hidden')).length;
const note = (root: HTMLElement) => root.querySelector('[data-testid="widget-table-left-out"]');

describe('a widget table on paper', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  it('prints the rows that fit and says how many were left out (Long table)', async () => {
    const { fixture, root, print } = await render(40);
    layOut(root, 352, evenRows(40));

    print.prepare();
    await fixture.whenStable();

    expect(printed(root)).toBe(12);
    expect(note(root)?.textContent?.trim()).toBe('28 rows not printed.');
    expect(note(root)?.classList).toContain('paper:block');
    expect(note(root)?.classList).toContain('hidden');
  });

  it('keeps a line for the note, so the last printed row is whole', async () => {
    const { fixture, root, print } = await render(5);
    layOut(root, 170, [64, 88, 112, 136, 300]);

    print.prepare();
    await fixture.whenStable();

    expect(printed(root)).toBe(4);
    expect(note(root)?.textContent?.trim()).toBe('1 row not printed.');
  });

  it('says it in Spanish', async () => {
    const { fixture, root, print } = await render(40, 'es');
    layOut(root, 352, evenRows(40));

    print.prepare();
    await fixture.whenStable();

    expect(note(root)?.textContent?.trim()).toBe('28 filas no se imprimieron.');
  });

  it('prints every row and no note when they all fit (Short table)', async () => {
    const { fixture, root, print } = await render(5);
    layOut(root, 400, evenRows(5));

    print.prepare();
    await fixture.whenStable();

    expect(printed(root)).toBe(5);
    expect(note(root)).toBeNull();
  });

  it('shows every row on screen, and measures again for the next print', async () => {
    const { fixture, root, print } = await render(40);
    layOut(root, 352, evenRows(40));
    print.prepare();
    await fixture.whenStable();

    print.restore();
    await fixture.whenStable();
    expect(printed(root)).toBe(40);
    expect(note(root)).toBeNull();

    layOut(root, 2000, evenRows(40));
    print.prepare();
    await fixture.whenStable();
    expect(printed(root)).toBe(40);
    expect(note(root)).toBeNull();
  });
});
