import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { configureApp, projectId, settle } from '../../../../testing/project-harness';
import { DashboardFilter, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartTooltips } from '../../../shared/ui';
import { encodeFilters } from '../data-view';
import { Context, all, answerLoad, answerQueries, clearOverlay, colorsOfWidgets, config, flushLayout, isLayout, isQuery, layoutUrlOf, layoutsOf, open, paperOn, result, row, text, wait, widget } from './indicators-testing';
import { layoutSaveDelayMs } from '../project-indicators';
import { WidgetGrid } from './widget-grid';

const rows = result([row('Cali', 75)]);
const left = widget('w1', { title: 'Left', layout: { x: 0, y: 0, w: 6, h: 4 } });
const right = widget('w2', { title: 'Right', layout: { x: 6, y: 0, w: 6, h: 4 } });

const item = (context: Context, id: string) => context.root.querySelector<HTMLElement>(`[data-widget-id="${id}"]`)!;
const place = (context: Context, id: string) => {
  const style = item(context, id).style;
  return { column: style.getPropertyValue('grid-column'), row: style.getPropertyValue('grid-row') };
};
const handle = (context: Context, id: string, kind: 'move' | 'resize') => item(context, id).querySelector<HTMLElement>(`[data-testid="widget-${kind}"]`)!;

function press(target: HTMLElement, key: string, shift = false): void {
  target.dispatchEvent(new KeyboardEvent('keydown', { key, shiftKey: shift, bubbles: true, cancelable: true }));
}

async function pressAndSettle(context: Context, target: HTMLElement, key: string, shift = false) {
  press(target, key, shift);
  await settle(context.harness);
}

/** The layout saves sent so far (add-dashboard-versions, design D8): one per pause, whatever the widgets it holds. */
const layoutSaves = (context: Context) => context.http.match(isLayout);

describe('the widget grid', () => {
  afterEach(clearOverlay);

  describe('reading order', () => {
    it('lists the widgets top to bottom, left to right, whatever order they come in', async () => {
      const lower = widget('w1', { title: 'Lower left', layout: { x: 0, y: 4, w: 6, h: 4 } });
      const upperRight = widget('w2', { title: 'Upper right', layout: { x: 6, y: 0, w: 6, h: 4 } });
      const upperLeft = widget('w3', { title: 'Upper left', layout: { x: 0, y: 0, w: 6, h: 4 } });
      const lowerRight = widget('w4', { title: 'Lower right', layout: { x: 6, y: 4, w: 6, h: 4 } });
      const context = await open('analyst', [lowerRight, lower, upperRight, upperLeft], rows);

      expect(all(context.root, 'widget-title').map(text)).toEqual(['Upper left', 'Upper right', 'Lower left', 'Lower right']);
      // Keyboard order is document order: the move handles follow it.
      expect(all(context.root, 'widget-move').map((h) => h.getAttribute('aria-label'))).toEqual(['Move Upper left', 'Move Upper right', 'Move Lower left', 'Move Lower right']);
    });

    it('places each widget on the 12-column grid from its layout', async () => {
      const context = await open('viewer', [widget('w1', { layout: { x: 3, y: 2, w: 5, h: 3 } })], rows);

      expect(place(context, 'w1')).toEqual({ column: '4 / span 5', row: '3 / span 3' });
    });

    it('gives a viewer a plain list of widgets, with no way to move them', async () => {
      const context = await open('viewer', [left, right], rows);

      expect(all(context.root, 'widget-move')).toHaveLength(0);
      expect(all(context.root, 'widget-resize')).toHaveLength(0);
      expect(item(context, 'w1').tagName).toBe('LI');
    });
  });

  describe('moving with the keyboard', () => {
    it('moves the widget one cell per arrow key, at once on screen', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '2 / span 4' });
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '2 / span 4' });
      expect(text(context.root.querySelector('[data-testid="grid-status"]'))).toBe('Left: column 1, row 2, 6 wide, 4 high.');
      layoutSaves(context); // drain
    });

    it('keeps the widget inside the grid', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowLeft');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 4' });
      expect(layoutSaves(context)).toHaveLength(0); // nothing moved, so nothing to save
    });

    it('moves the widgets it lands on downward', async () => {
      const context = await open('analyst', [left, right], rows);

      for (let i = 0; i < 6; i++) {
        await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowRight');
      }

      expect(place(context, 'w1')).toEqual({ column: '7 / span 6', row: '1 / span 4' });
      expect(place(context, 'w2')).toEqual({ column: '7 / span 6', row: '5 / span 4' });
      // Reading order follows: the pushed widget is now below.
      expect(all(context.root, 'widget-title').map(text)).toEqual(['Left', 'Right']);
      layoutSaves(context);
    });

    it('resizes with shift and the arrows, and with the corner handle', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown', true);
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 5' });
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowLeft');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 5', row: '1 / span 5' });
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 5', row: '1 / span 4' });
      layoutSaves(context);
    });

    it('does not resize below one cell, or past the grid', async () => {
      const context = await open('analyst', [widget('w1', { layout: { x: 11, y: 0, w: 1, h: 1 } })], rows);

      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowLeft');
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowRight');
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '12 / span 1', row: '1 / span 1' });
      expect(layoutSaves(context)).toHaveLength(0);
    });

    it('ignores other keys', async () => {
      const context = await open('analyst', [left], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'Enter');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'a');

      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 4' });
    });
  });

  describe('saving', () => {
    it('saves three arrow presses with one put of the layout, 400 ms after the last key', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      expect(layoutSaves(context)).toHaveLength(0); // still waiting

      await wait(layoutSaveDelayMs + 100);
      const requests = layoutSaves(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.url).toBe(layoutUrlOf('d1'));
      expect(requests[0].request.body).toEqual({ widgets: [{ id: 'w1', layout: { x: 0, y: 3, w: 6, h: 4 } }] });
      flushLayout(requests[0], [left, right]);
      await settle(context.harness);

      await wait(layoutSaveDelayMs + 100);
      expect(layoutSaves(context)).toHaveLength(0);
      expect(context.http.match((r) => r.method === 'PATCH')).toHaveLength(0); // no widget is patched for a drag
    }, 10000);

    it('saves a resize with one put of the layout', async () => {
      const context = await open('analyst', [left], rows);

      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowRight');
      await wait(layoutSaveDelayMs + 100);

      const requests = layoutSaves(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body).toEqual({ widgets: [{ id: 'w1', layout: { x: 0, y: 0, w: 7, h: 5 } }] });
      flushLayout(requests[0], [left]);
    }, 10000);

    it('saves a move and the widget it pushed down in one put (Drag that pushes other widgets)', async () => {
      const context = await open('analyst', [left, right], rows);

      for (let i = 0; i < 6; i++) {
        await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowRight');
      }
      await wait(layoutSaveDelayMs + 100);

      const requests = layoutSaves(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body.widgets).toHaveLength(2);
      expect(layoutsOf(requests[0])).toEqual({ w1: { x: 6, y: 0, w: 6, h: 4 }, w2: { x: 6, y: 4, w: 6, h: 4 } });
      flushLayout(requests[0], [left, right]);
    }, 10000);

    it('saves the moves of several widgets made within the pause in one put, each with its latest place', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w2', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await wait(layoutSaveDelayMs + 100);

      const requests = layoutSaves(context);
      expect(requests).toHaveLength(1);
      expect(layoutsOf(requests[0])).toEqual({ w1: { x: 0, y: 2, w: 6, h: 4 }, w2: { x: 6, y: 1, w: 6, h: 4 } });
      flushLayout(requests[0], [left, right]);
    }, 10000);

    it('saves two moves apart by more than the pause in two puts, each with the widgets it moved (Two drags)', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await wait(layoutSaveDelayMs + 100);
      const [first] = layoutSaves(context);
      expect(first.request.body).toEqual({ widgets: [{ id: 'w1', layout: { x: 0, y: 1, w: 6, h: 4 } }] });
      flushLayout(first, [left, right]);
      await settle(context.harness);

      await pressAndSettle(context, handle(context, 'w2', 'move'), 'ArrowDown');
      await wait(layoutSaveDelayMs + 100);
      const [second, ...more] = layoutSaves(context);
      expect(more).toHaveLength(0);
      expect(second.request.body).toEqual({ widgets: [{ id: 'w2', layout: { x: 6, y: 1, w: 6, h: 4 } }] });
      flushLayout(second, [left, right]);
    }, 10000);

    it('does not ask for the widget\'s rows again after a move', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await wait(layoutSaveDelayMs + 100);

      expect(context.http.match((r) => r.method === 'POST' && r.url.endsWith('/query'))).toHaveLength(0);
      layoutSaves(context).forEach((r) => flushLayout(r, [left, right]));
    }, 10000);

    it('shows what the server has when a save fails, reading the dashboard again', async () => {
      const context = await open('analyst', [left, right], rows);

      for (let i = 0; i < 6; i++) {
        await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowRight');
      }
      await wait(layoutSaveDelayMs + 100);
      layoutSaves(context)[0].flush({ title: 'x' }, { status: 403, statusText: 'Forbidden' });
      await settle(context.harness);

      await answerLoad(context, [left, right]); // the overview and the dashboard, read again
      await answerQueries(context, rows);
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 4' });
      expect(place(context, 'w2')).toEqual({ column: '7 / span 6', row: '1 / span 4' });
    }, 10000);
  });

  describe('dragging with the pointer', () => {
    function mouse(type: string, target: EventTarget, x: number, y: number): void {
      target.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: y, screenX: x, screenY: y, button: 0, buttons: type === 'mouseup' ? 0 : 1, detail: 1 }));
    }

    it('moves the widget by the cells the pointer travelled and saves the new place', async () => {
      const context = await open('analyst', [left, right], rows);
      const grip = handle(context, 'w1', 'move');

      // 1200 px wide grid when nothing is measured: a cell is 101.3 px wide and 88 px tall with its gap.
      mouse('mousedown', grip, 100, 100);
      mouse('mousemove', document, 120, 110);
      mouse('mousemove', document, 100, 100 + 88 * 4 + 5);
      await settle(context.harness);
      expect(context.root.querySelector('[data-testid="widget-ghost"]')).not.toBeNull();
      mouse('mouseup', document, 100, 100 + 88 * 4 + 5);
      await settle(context.harness);

      expect(context.root.querySelector('[data-testid="widget-ghost"]')).toBeNull();
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '5 / span 4' });
      await wait(layoutSaveDelayMs + 100);
      const requests = layoutSaves(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body).toEqual({ widgets: [{ id: 'w1', layout: { x: 0, y: 4, w: 6, h: 4 } }] });
      flushLayout(requests[0], [left, right]);
    }, 10000);

    it('closes the tooltips of the page when a tile starts to move, and when it is resized', async () => {
      const context = await open('analyst', [left], rows);
      const closeAll = vi.spyOn(TestBed.inject(ChartTooltips), 'closeAll');

      mouse('mousedown', handle(context, 'w1', 'move'), 100, 100);
      mouse('mousemove', document, 120, 110);
      mouse('mousemove', document, 140, 120);
      mouse('mouseup', document, 140, 120);
      await settle(context.harness);
      expect(closeAll).toHaveBeenCalledTimes(1);

      mouse('mousedown', handle(context, 'w1', 'resize'), 500, 300);
      mouse('mousemove', document, 520, 310);
      mouse('mousemove', document, 540, 320);
      mouse('mouseup', document, 540, 320);
      await settle(context.harness);
      expect(closeAll).toHaveBeenCalledTimes(2);
      await wait(layoutSaveDelayMs + 100);
      layoutSaves(context).forEach((request) => flushLayout(request, [left]));
    }, 10000);

    it('resizes the widget by the cells the corner handle travelled', async () => {
      const context = await open('analyst', [left], rows);
      const corner = handle(context, 'w1', 'resize');

      mouse('mousedown', corner, 500, 300);
      mouse('mousemove', document, 520, 310);
      mouse('mousemove', document, 500 + 101 * 2, 300 + 88);
      mouse('mouseup', document, 500 + 101 * 2, 300 + 88);
      await settle(context.harness);

      expect(place(context, 'w1')).toEqual({ column: '1 / span 8', row: '1 / span 5' });
      await wait(layoutSaveDelayMs + 100);
      const requests = layoutSaves(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body).toEqual({ widgets: [{ id: 'w1', layout: { x: 0, y: 0, w: 8, h: 5 } }] });
      flushLayout(requests[0], [left]);
    }, 10000);
  });
});

// Dashboard filters (dashboard filters, design D7): each widget's query gets the active filters it does not ignore, and the widget
// says which active filters did not narrow it.
describe('the widget grid with dashboard filters', () => {
  afterEach(clearOverlay);

  const municipality: DashboardFilter = { column: 'municipality', label: 'Municipality', kind: 'text', default: null };
  const round: DashboardFilter = { column: 'round', label: 'Round', kind: 'number', default: null };
  const cali = `f=${encodeFilters([{ column: 'municipality', op: 'in', values: ['Cali'] }])}`;
  const note = (context: Context, id: string) => text(item(context, id).querySelector('[data-testid="widget-not-filtered"]'));
  const bodyOf = (requests: { request: { body: { config: { indicator: string }; dashboardFilters?: unknown } } }[], indicator: string) =>
    requests.find((r) => r.request.body.config.indicator === indicator)!.request.body;

  it('re-queries every widget once with the new filters, and drops the queries still on their way', async () => {
    const context = await open('viewer', [left, right], null, {}, { filters: [municipality] });
    const first = context.http.match(isQuery);
    expect(first).toHaveLength(2);

    await TestBed.inject(Router).navigateByUrl(`/projects/${projectId}/dashboards/d1?${cali}`);
    await settle(context.harness);
    const second = await answerQueries(context, rows);

    expect(first.every((r) => r.cancelled)).toBe(true);
    expect(second).toHaveLength(2);
    expect(second.map((r) => r.request.body.dashboardFilters)).toEqual([
      [{ column: 'municipality', kind: 'text', values: ['Cali'] }],
      [{ column: 'municipality', kind: 'text', values: ['Cali'] }],
    ]);
  });

  it('names the filters a widget\'s table did not take (Indicator without the column, Column of another kind)', async () => {
    const both = `f=${encodeFilters([
      { column: 'municipality', op: 'in', values: ['Cali'] },
      { column: 'round', op: 'range', min: '1', max: '2' },
    ])}`;
    const context = await open('viewer', [left, right], null, {}, { filters: [municipality, round], query: both });
    const requests = context.http.match(isQuery);
    requests[0].flush(result([row('Cali', 75)], { notFiltered: ['round'] }));
    requests[1].flush(result([row('Cali', 75)], { notFiltered: ['municipality', 'round'] }));
    await settle(context.harness);

    expect(note(context, 'w1')).toBe('Not filtered by: Round');
    expect(note(context, 'w2')).toBe('Not filtered by: Municipality, Round');
    expect(item(context, 'w1').querySelector('[data-testid="widget-missing"]')).toBeNull();
  });

  it('leaves an ignored filter out of the widget\'s query and names it (Ignored filter)', async () => {
    const total = widget('w3', {
      title: 'Households, all municipalities',
      chartType: 'stat',
      config: config({ indicator: 'households_visited', groupBy: null, ignoredDashboardFilters: ['municipality'] }),
      layout: { x: 0, y: 4, w: 3, h: 2 },
    });
    const context = await open('viewer', [left, total], null, {}, { filters: [municipality], query: cali });
    const requests = await answerQueries(context, rows);

    expect(bodyOf(requests, 'households_visited').dashboardFilters).toBeUndefined();
    expect(bodyOf(requests, 'coverage').dashboardFilters).toEqual([{ column: 'municipality', kind: 'text', values: ['Cali'] }]);
    expect(note(context, 'w3')).toBe('Not filtered by: Municipality');
    expect(note(context, 'w1')).toBe('');
  });

  it('says nothing about a filter without a value (Filter without a value)', async () => {
    const context = await open('viewer', [left], null, {}, { filters: [municipality] });
    const requests = await answerQueries(context, rows);

    expect(requests[0].request.body.dashboardFilters).toBeUndefined();
    expect(note(context, 'w1')).toBe('');
  });

  it('says nothing about filters on a widget that draws nothing: not published, or a missing column', async () => {
    const context = await open('viewer', [left, right], null, {}, { filters: [municipality], query: cali });
    const requests = context.http.match(isQuery);
    requests[0].flush(result([], { published: false, notFiltered: ['municipality'] }));
    requests[1].flush(result([], { missing: { indicator: false, columns: ['round'] }, notFiltered: ['municipality'] }));
    await settle(context.harness);

    expect(note(context, 'w1')).toBe('');
    expect(note(context, 'w2')).toBe('');
  });
});

describe('the widget grid on paper (dashboard printing)', () => {
  afterEach(() => {
    clearOverlay();
    document.documentElement.classList.remove('tmz-printing');
    document.getElementById('tmz-print-page')?.remove();
  });

  const grid = (context: Context) => context.root.querySelector<HTMLElement>('[data-testid="widget-grid"]')!;

  it('keeps the 12 columns while the page prepares the print, and goes back after (Twelve columns on paper)', async () => {
    const context = await open('analyst', [left, right], rows);
    expect(grid(context).classList).not.toContain('tmz-paper');

    const print = await paperOn(context);
    expect(grid(context).classList).toContain('tmz-paper');
    expect(place(context, 'w2')).toEqual({ column: '7 / span 6', row: '1 / span 4' });

    print.restore();
    await settle(context.harness);
    expect(grid(context).classList).not.toContain('tmz-paper');
  });

  it('starts each tile 2px into its grid area on paper, clear of what a scaled-down sheet paints of the next one (design D2)', async () => {
    const context = await open('analyst', [left, right], rows);
    expect(getComputedStyle(item(context, 'w1')).paddingTop).not.toBe('2px');

    const print = await paperOn(context);
    expect(getComputedStyle(item(context, 'w1')).paddingTop).toBe('2px');
    expect(getComputedStyle(item(context, 'w2')).paddingTop).toBe('2px');

    print.restore();
    await settle(context.harness);
    expect(getComputedStyle(item(context, 'w1')).paddingTop).not.toBe('2px');
  });

  it('leaves every widget control off the paper, and the tiles white without a shadow (Content of a printed dashboard)', async () => {
    const context = await open('analyst', [left], rows);
    const tile = item(context, 'w1');

    expect(handle(context, 'w1', 'move').classList).toContain('paper:hidden');
    expect(handle(context, 'w1', 'resize').classList).toContain('paper:hidden');
    expect(tile.querySelector('[data-testid="widget-table-toggle"]')!.classList).toContain('paper:hidden');
    expect(tile.querySelector('tmz-dropdown-menu')!.classList).toContain('paper:hidden');
    expect(tile.querySelector('[data-testid="widget-title"]')!.closest('.paper\:hidden')).toBeNull();
    const article = tile.querySelector('article')!;
    expect(article.classList).toContain('paper:bg-white');
    expect(article.classList).toContain('paper:shadow-none');
  });

  it('lays out as before where no dashboard page provides paper mode', async () => {
    configureApp();
    const fixture = TestBed.createComponent(WidgetGrid);
    fixture.componentRef.setInput('widgets', []);
    fixture.componentRef.setInput('projectId', projectId);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('[data-testid="widget-grid"]')!.classList).not.toContain('tmz-paper');
  });
});

// The colors of the dashboard shown reach every widget (add-dashboard-palettes, design D7): these check what each widget resolves from
// them, and the charts' own specs check that the marks are drawn in it.
describe('the colors of the dashboard shown', () => {
  afterEach(clearOverlay);

  const water = (id: string, display?: WidgetConfig['display']) => widget(id, { config: config({ display }) });

  it('draws a widget of a dashboard with the palette Earth in the first color of Earth, and on its scale (Palette changed)', async () => {
    const context = await open('viewer', [left], rows, {}, { colors: { palette: 'earth', valueColors: [] } });

    const [colors] = colorsOfWidgets(context);
    expect(colors.accent).toBe('var(--tmz-pal-earth-1)');
    expect(colors.ramp).toBe('orange');
  });

  it('draws a widget in the color of its own place in the palette (Column chart in color 3)', async () => {
    const context = await open('viewer', [water('w1', { color: 3 }), water('w2')], rows, {}, { colors: { palette: 'vivid', valueColors: [] } });

    expect(colorsOfWidgets(context).map((colors) => colors.accent)).toEqual(['var(--tmz-pal-vivid-3)', 'var(--tmz-pal-vivid-1)']);
  });

  it('gives every widget the colors kept by value of the dashboard', async () => {
    const colors = { palette: 'tamiza' as const, valueColors: [{ column: 'sex', value: 'Female', color: 5 }] };
    const context = await open('viewer', [left, right], rows, {}, { colors });

    for (const widgetColors of colorsOfWidgets(context)) {
      expect(widgetColors.forValues('sex', [{ value: 'Male', kind: 'value' }, { value: 'Female', kind: 'value' }])).toEqual(['var(--tmz-pal-tamiza-1)', 'var(--tmz-pal-tamiza-5)']);
    }
  });

  it('draws a dashboard saved before palettes in the colors it has today (Dashboard saved before palettes)', async () => {
    const context = await open('viewer', [left], rows);

    const [colors] = colorsOfWidgets(context);
    expect(colors.accent).toBe('var(--tmz-pal-tamiza-1)');
    expect(colors.ramp).toBe('blue');
    expect(colors.kept('municipality', 'Cali')).toBeNull();
  });

  it('sends no query for a color: the widget\'s query leaves its display out (Widget saved before colors)', async () => {
    const context = await open('viewer', [water('w1', { color: 3 })], null, {}, { colors: { palette: 'earth', valueColors: [] } });

    const [request] = context.http.match(isQuery);
    expect(request.request.body.config).not.toHaveProperty('display');
  });
});
