import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { pressKey, tick } from '../../../../testing/cdk';
import { configureApp, projectId, settle } from '../../../../testing/project-harness';
import { DashboardFilter, Widget, WidgetConfig } from '../../../core/projects/indicators.service';
import { ChartTooltips } from '../../../shared/ui';
import { encodeFilters } from '../data-view';
import { Context, all, answerLoad, answerQueries, clearOverlay, colorsOfWidgets, config, flushLayout, indicator, isLayout, isQuery, layoutUrlOf, layoutsOf, menuItem, open, openWidgetMenu, paperOn, result, row, text, toggleTable, wait, widget } from './indicators-testing';
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
    expect(tile.querySelector('[data-testid="widget-menu"]')!.closest('tmz-dropdown-menu')!.classList).toContain('paper:hidden');
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
describe('the widget menu in the grid', () => {
  afterEach(clearOverlay);

  const status = (context: Context) => text(context.root.querySelector('[data-testid="grid-status"]'));
  const hasTable = (context: Context, id: string) => item(context, id).querySelector('[data-testid="widget-table"]') !== null;

  it('switches between the chart and the table, checking the item and announcing each view (Switched to a table, Back to the chart)', async () => {
    const context = await open('viewer', [left, right], rows);

    await toggleTable(context, 1);
    expect(hasTable(context, 'w2')).toBe(true);
    expect(hasTable(context, 'w1')).toBe(false);
    expect(status(context)).toBe('“Right” is shown as a table');
    await openWidgetMenu(context, 1);
    expect(menuItem('widget-show-table')!.getAttribute('aria-checked')).toBe('true');
    menuItem('widget-show-table')!.click();
    await settle(context.harness);

    expect(hasTable(context, 'w2')).toBe(false);
    expect(status(context)).toBe('“Right” is shown as a chart');
    await openWidgetMenu(context, 1);
    expect(menuItem('widget-show-table')!.getAttribute('aria-checked')).toBe('false');
  });

  it('opens from the keyboard, moves through its items, and closes on Escape with focus on its button (Keyboard)', async () => {
    const context = await open('analyst', [left], rows);
    const trigger = context.root.querySelector<HTMLElement>('[data-testid="widget-menu"]')!;

    trigger.focus();
    pressKey(trigger, 'ArrowDown');
    await settle(context.harness);
    const menu = document.querySelector<HTMLElement>('[role="menu"]')!;
    const order = ['widget-show-table', 'widget-export-png', 'widget-export-svg', 'widget-export-xlsx', 'widget-edit', 'widget-remove'];
    expect(document.activeElement).toBe(menuItem(order[0]));
    for (const id of order.slice(1)) {
      pressKey(menu, 'ArrowDown');
      expect(document.activeElement).toBe(menuItem(id));
    }

    pressKey(menu, 'Escape');
    await settle(context.harness);
    await tick();
    expect(document.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger);
  });

  it('shows a viewer the menu without Edit widget and Remove widget, and an analyst with them', async () => {
    const viewer = await open('viewer', [left], rows);
    await openWidgetMenu(viewer);
    expect(menuItem('widget-edit')).toBeNull();
    expect(menuItem('widget-remove')).toBeNull();
    expect(menuItem('widget-export-png')).not.toBeNull();
  });
});

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

describe('the texts of a widget in its tile (add-widget-context, design D5, D6)', () => {
  afterEach(clearOverlay);

  const subtitle = '¿En qué lugares ha visto teatro en el último año? (E4)';
  const withDisplay = (id: string, display: NonNullable<WidgetConfig['display']>, overrides: Partial<Widget> = {}) =>
    widget(id, { title: `Title ${id}`, config: config({ display }), ...overrides });
  const headerOf = (context: Context, id: string) => item(context, id).querySelector('header')!;
  const subtitleOf = (context: Context, id: string) => item(context, id).querySelector<HTMLElement>('[data-testid="widget-subtitle"]');
  const articleOf = (context: Context, id: string) => item(context, id).querySelector('article')!;

  it('shows the subtitle under the title, in a header of two rows (Subtitle under the title)', async () => {
    const context = await open('viewer', [withDisplay('w1', { subtitle })], rows);

    const line = subtitleOf(context, 'w1')!;
    expect(text(line)).toBe(subtitle);
    expect(line.id).toBe('widget-subtitle-w1');
    const header = headerOf(context, 'w1');
    expect(header.classList.contains('flex-col')).toBe(true);
    expect(header.children[0].contains(item(context, 'w1').querySelector('[data-testid="widget-title"]'))).toBe(true);
    expect(header.children[1]).toBe(line);
    expect(articleOf(context, 'w1').getAttribute('aria-describedby')).toBe('widget-subtitle-w1');
    expect(articleOf(context, 'w1').getAttribute('aria-labelledby')).toBe('widget-title-w1');
  });

  it('keeps the title row whole when a widget has a subtitle: the handle, the title and the menu', async () => {
    const context = await open('analyst', [withDisplay('w1', { subtitle })], rows);

    const titleRow = headerOf(context, 'w1').children[0];
    expect(titleRow.querySelector('[data-testid="widget-move"]')).not.toBeNull();
    expect(titleRow.querySelector('[data-testid="widget-title"]')).not.toBeNull();
    expect(titleRow.querySelector('[data-testid="widget-menu"]')).not.toBeNull();
    // The handle still moves the tile when it sits in the first row of the two-row header.
    await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
    expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '2 / span 4' });
    layoutSaves(context);
  });

  it('shows the subtitle of a stat tile under its title, above its figure (Subtitle of a stat tile)', async () => {
    const stat = widget('w1', { chartType: 'stat', title: 'Centro cultural', layout: { x: 0, y: 0, w: 4, h: 3 }, config: config({ groupBy: null, display: { subtitle: '% de quienes asistieron (E4)' } }) });
    const context = await open('viewer', [stat], result([row(null, 0.2422)]));

    const line = subtitleOf(context, 'w1')!;
    expect(text(line)).toBe('% de quienes asistieron (E4)');
    expect(headerOf(context, 'w1').contains(line)).toBe(true);
    expect(item(context, 'w1').querySelector('[data-testid="stat"]')!.compareDocumentPosition(line) & Node.DOCUMENT_POSITION_PRECEDING).toBeTruthy();
  });

  it('holds the whole text for pointing, and cuts it at two lines, or one on a tile two rows high (Whole text on pointing)', async () => {
    const tall = withDisplay('w1', { subtitle: 'A'.repeat(180) });
    const low = withDisplay('w2', { subtitle }, { layout: { x: 6, y: 0, w: 6, h: 2 } });
    const context = await open('viewer', [tall, low], rows);

    expect(subtitleOf(context, 'w1')!.getAttribute('title')).toBe('A'.repeat(180));
    expect(text(subtitleOf(context, 'w1'))).toBe('A'.repeat(180));
    expect(subtitleOf(context, 'w1')!.classList.contains('line-clamp-2')).toBe(true);
    expect(subtitleOf(context, 'w1')!.classList.contains('line-clamp-1')).toBe(false);
    expect(subtitleOf(context, 'w2')!.getAttribute('title')).toBe(subtitle);
    expect(subtitleOf(context, 'w2')!.classList.contains('line-clamp-1')).toBe(true);
    expect(subtitleOf(context, 'w2')!.classList.contains('line-clamp-2')).toBe(false);
    // The clamp is lifted on paper, where texts print whole.
    expect(subtitleOf(context, 'w1')!.classList.contains('paper:line-clamp-none')).toBe(true);
  });

  it('reads a blank subtitle as none', async () => {
    const context = await open('viewer', [withDisplay('w1', { subtitle: '   ' })], rows);

    expect(subtitleOf(context, 'w1')).toBeNull();
    expect(articleOf(context, 'w1').hasAttribute('aria-describedby')).toBe(false);
  });

  it('keeps the header of a widget with no subtitle as it was: one row, no subtitle, no description (Widgets saved before)', async () => {
    const context = await open('analyst', [left, withDisplay('w2', { color: 3 }, { layout: { x: 6, y: 0, w: 6, h: 4 } })], rows);

    for (const id of ['w1', 'w2']) {
      const header = headerOf(context, id);
      expect(header.className).toBe('flex shrink-0 items-start gap-1 border-b border-default px-3 pt-2 pb-1.5');
      expect(Array.from(header.children).map((child) => child.tagName.toLowerCase())).toEqual(['button', 'div', 'tmz-dropdown-menu']);
      expect(header.children[1].querySelector('h3')).not.toBeNull();
      expect(subtitleOf(context, id)).toBeNull();
      expect(articleOf(context, id).hasAttribute('aria-describedby')).toBe(false);
      expect(item(context, id).querySelector('[data-testid="widget-description"]')).toBeNull();
    }
  });

  // The type scale (improve-widget-text-hierarchy, design D1, task 2.2)

  it('draws the title at 15 px, weight 600, on up to two lines, with its whole text (Title of two lines)', async () => {
    const title = '2.6. Lo que pagó por la última boleta: mediana y dispersión';
    const context = await open('viewer', [widget('w1', { title, layout: { x: 0, y: 0, w: 3, h: 4 } })], rows);

    const h3 = item(context, 'w1').querySelector<HTMLElement>('[data-testid="widget-title"]')!;
    for (const name of ['text-[15px]', 'leading-[1.35]', 'font-semibold', 'text-heading', 'line-clamp-2', 'break-words', 'paper:line-clamp-none']) {
      expect(h3.classList.contains(name), name).toBe(true);
    }
    expect(h3.classList.contains('truncate')).toBe(false);
    expect(h3.classList.contains('font-medium')).toBe(false);
    expect(text(h3)).toBe(title);
    expect(h3.getAttribute('title')).toBe(title);
  });

  it('keeps the whole text of a title cut at two lines in the heading, in title and in the accessible name (Title longer than two lines)', async () => {
    const title = 'T'.repeat(120);
    const context = await open('viewer', [widget('w1', { title, layout: { x: 0, y: 0, w: 2, h: 4 } })], rows);

    const h3 = item(context, 'w1').querySelector<HTMLElement>('[data-testid="widget-title"]')!;
    expect(h3.textContent!.trim()).toBe(title);
    expect(h3.getAttribute('title')).toBe(title);
    expect(h3.classList.contains('line-clamp-2')).toBe(true);
    expect(articleOf(context, 'w1').getAttribute('aria-labelledby')).toBe(h3.id);
  });

  it('puts the handle, the title and the menu on one row that stays as high as the buttons, with the padding of the scale', async () => {
    const context = await open('analyst', [withDisplay('w1', { subtitle })], rows);

    const header = headerOf(context, 'w1');
    expect(header.className).toContain('pt-2');
    expect(header.className).toContain('pb-1.5');
    expect(header.children[0].classList.contains('items-start')).toBe(true);
    const box = header.querySelector('h3')!.parentElement!;
    expect(box.classList.contains('min-h-9')).toBe(true);
    expect(box.classList.contains('items-center')).toBe(true);
  });

  it('draws the subtitle at 13 px in the darker body gray, one line on a tile two rows high and two on a taller one (Subtitle size and color)', async () => {
    const tall = withDisplay('w1', { subtitle });
    const low = withDisplay('w2', { subtitle }, { layout: { x: 6, y: 0, w: 6, h: 2 } });
    const context = await open('viewer', [tall, low], rows);

    for (const id of ['w1', 'w2']) {
      const line = subtitleOf(context, id)!;
      for (const name of ['text-[13px]', 'leading-[1.45]', 'text-body-strong', 'paper:line-clamp-none']) {
        expect(line.classList.contains(name), `${id} ${name}`).toBe(true);
      }
      expect(line.classList.contains('text-xs')).toBe(false);
      expect(line.classList.contains('text-body')).toBe(false);
    }
    expect(subtitleOf(context, 'w1')!.classList.contains('line-clamp-2')).toBe(true);
    expect(subtitleOf(context, 'w2')!.classList.contains('line-clamp-1')).toBe(true);
  });

  // Compact stat tiles (design D6, task 2.3)

  const statTile = (id: string, w: number, h: number, extra: Partial<Widget> = {}) =>
    widget(id, { chartType: 'stat', title: `Tile ${id}`, layout: { x: 0, y: 0, w, h }, config: config({ groupBy: null, display: { subtitle } }), ...extra });
  const visibleUnderTitle = (context: Context, id: string) => !subtitleOf(context, id)!.classList.contains('hidden');

  it('keeps the subtitle under the title of a stat tile 3 columns wide and 3 rows high, and of one 3 wide and 6 high (Tile 3 columns wide)', async () => {
    const context = await open('viewer', [statTile('w1', 3, 3), statTile('w2', 3, 6, { layout: { x: 3, y: 0, w: 3, h: 6 } })], result([row(null, 0.2422)]));

    expect(visibleUnderTitle(context, 'w1')).toBe(true);
    expect(visibleUnderTitle(context, 'w2')).toBe(true);
    expect(subtitleOf(context, 'w1')!.classList.contains('hidden')).toBe(false);
  });

  it('keeps the subtitle under the title of a stat tile 4 columns wide and 3 rows high (Tile 4 columns wide and 3 rows high)', async () => {
    const context = await open('viewer', [statTile('w1', 4, 3)], result([row(null, 0.2422)]));

    expect(visibleUnderTitle(context, 'w1')).toBe(true);
    expect(subtitleOf(context, 'w1')!.classList.contains('line-clamp-2')).toBe(true);
  });

  it('takes the subtitle of a stat tile 4 columns wide and 2 rows high off the screen and prints it under the title, still describing the tile (Tile 4 columns wide and 2 rows high)', async () => {
    const context = await open('viewer', [statTile('w1', 4, 2), statTile('w2', 3, 2, { layout: { x: 4, y: 0, w: 3, h: 2 } })], result([row(null, 0.2422)]));

    for (const id of ['w1', 'w2']) {
      const line = subtitleOf(context, id)!;
      expect(line.classList.contains('hidden'), id).toBe(true);
      // Not on paper either: its text is the foot's Detail line there, under the figure (task 9.3). `paper:line-clamp-none` would show
      // it again, since it sets `display: block` after `hidden`, so a compact tile has `paper:hidden` and not that class.
      expect(line.classList.contains('paper:hidden'), id).toBe(true);
      expect(line.classList.contains('paper:block'), id).toBe(false);
      expect(line.classList.contains('paper:line-clamp-none'), id).toBe(false);
      expect(line.classList.contains('line-clamp-1')).toBe(false);
      expect(line.classList.contains('line-clamp-2')).toBe(false);
      expect(articleOf(context, id).getAttribute('aria-describedby')).toBe(`widget-subtitle-${id}`);
      expect(text(line)).toBe(subtitle);
    }
  });

  it('does not take the subtitle of a chart 4 columns wide and 2 rows high off the screen', async () => {
    const context = await open('viewer', [withDisplay('w1', { subtitle }, { layout: { x: 0, y: 0, w: 4, h: 2 } })], rows);

    expect(visibleUnderTitle(context, 'w1')).toBe(true);
    expect(subtitleOf(context, 'w1')!.classList.contains('line-clamp-1')).toBe(true);
  });

  it('counts only the height, with no breakpoint class, so a tile of 2 rows is compact on a phone and a 3 by 3 tile keeps its subtitle there (Phone)', async () => {
    const context = await open('viewer', [statTile('w1', 4, 2), statTile('w2', 3, 3, { layout: { x: 4, y: 0, w: 3, h: 3 } })], result([row(null, 0.2422)]));

    for (const id of ['w1', 'w2']) {
      expect(Array.from(subtitleOf(context, id)!.classList).filter((name) => name.startsWith('md:') || name.startsWith('sm:'))).toEqual([]);
    }
    expect(visibleUnderTitle(context, 'w1')).toBe(false);
    expect(visibleUnderTitle(context, 'w2')).toBe(true);
  });

  describe('the description button', () => {
    const described = indicator('coverage', { label: 'Coverage', description: 'Share of households that\nwatched a play.' });
    const button = (context: Context, id: string) => item(context, id).querySelector<HTMLButtonElement>('[data-testid="widget-description"]');
    const panel = () => document.querySelector<HTMLElement>('[data-testid="widget-description-panel"]');
    const offering = (id: string, extra: Partial<Widget> = {}) => withDisplay(id, { showDescription: true }, extra);
    const opened = async (context: Context, id: string) => {
      button(context, id)!.click();
      await settle(context.harness);
      await tick();
    };

    it('opens the indicator\'s description next to the button (Description opened)', async () => {
      const context = await open('viewer', [offering('w1')], rows, { indicators: [described] });
      const trigger = button(context, 'w1')!;
      expect(trigger.getAttribute('aria-label')).toBe('About Coverage');
      expect(trigger.getAttribute('aria-expanded')).toBe('false');
      expect(panel()).toBeNull();

      await opened(context, 'w1');

      expect(panel()).not.toBeNull();
      expect(text(panel()!.querySelector('[data-testid="widget-description-text"]'))).toBe('Share of households that watched a play.');
      expect(trigger.getAttribute('aria-expanded')).toBe('true');
      expect(trigger.getAttribute('aria-controls')).toBe(panel()!.id);
    });

    it('sits in the header before the menu, and is hidden on paper', async () => {
      const context = await open('viewer', [offering('w1')], rows, { indicators: [described] });

      const kinds = Array.from(headerOf(context, 'w1').children).map((child) => child.tagName.toLowerCase());
      expect(kinds).toEqual(['div', 'app-description-popover', 'tmz-dropdown-menu']);
      expect(headerOf(context, 'w1').querySelector('app-description-popover')!.classList.contains('paper:hidden')).toBe(true);
    });

    it('has no button for a widget that does not offer it, or an indicator with no description (Indicator without a description)', async () => {
      const none = indicator('coverage', { label: 'Coverage', description: null });
      const blank = indicator('target', { label: 'Target', description: '   ' });
      const plain = widget('w3', { config: config({ indicator: 'coverage' }) });
      const context = await open('viewer', [offering('w1'), offering('w2', { config: config({ indicator: 'target', display: { showDescription: true } }) }), plain], rows, { indicators: [none, blank, described] });

      expect(button(context, 'w1')).toBeNull();
      expect(button(context, 'w2')).toBeNull();
      expect(button(context, 'w3')).toBeNull();
    });

    it('has no button for a widget whose indicator the project no longer lists', async () => {
      const context = await open('viewer', [offering('w1')], rows, { indicators: [] });

      expect(button(context, 'w1')).toBeNull();
    });

    it('opens one description at a time', async () => {
      const second = indicator('target', { label: 'Target', description: 'The goal for the round.' });
      const context = await open('viewer', [offering('w1'), offering('w2', { config: config({ indicator: 'target', display: { showDescription: true } }), layout: { x: 6, y: 0, w: 6, h: 4 } })], rows, { indicators: [described, second] });

      await opened(context, 'w1');
      await opened(context, 'w2');

      expect(document.querySelectorAll('[data-testid="widget-description-panel"]')).toHaveLength(1);
      expect(text(panel()!.querySelector('[data-testid="widget-description-text"]'))).toBe('The goal for the round.');
      expect(button(context, 'w1')!.getAttribute('aria-expanded')).toBe('false');
      expect(button(context, 'w2')!.getAttribute('aria-expanded')).toBe('true');
    });
  });
});
