import { settle } from '../../../../testing/project-harness';
import { Layout } from '../../../core/projects/indicators.service';
import { Context, all, answerQueries, clearOverlay, indicatorsUrl, isPatch, open, result, row, text, wait, widget } from './indicators-testing';
import { layoutSaveDelayMs } from '../project-indicators';

const rows = result([row('coverage', 'Cali', 75)]);
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

const patches = (context: Context) => context.http.match(isPatch);

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
      patches(context); // drain
    });

    it('keeps the widget inside the grid', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowLeft');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 4' });
      expect(patches(context)).toHaveLength(0); // nothing moved, so nothing to save
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
      patches(context);
    });

    it('resizes with shift and the arrows, and with the corner handle', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown', true);
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 5' });
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowLeft');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 5', row: '1 / span 5' });
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '1 / span 5', row: '1 / span 4' });
      patches(context);
    });

    it('does not resize below one cell, or past the grid', async () => {
      const context = await open('analyst', [widget('w1', { layout: { x: 11, y: 0, w: 1, h: 1 } })], rows);

      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowLeft');
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowRight');
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowUp');
      expect(place(context, 'w1')).toEqual({ column: '12 / span 1', row: '1 / span 1' });
      expect(patches(context)).toHaveLength(0);
    });

    it('ignores other keys', async () => {
      const context = await open('analyst', [left], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'Enter');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'a');

      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 4' });
    });
  });

  describe('saving', () => {
    it('saves a move with one patch of the layout, 400 ms after the last key', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      expect(patches(context)).toHaveLength(0); // still waiting

      await wait(layoutSaveDelayMs + 100);
      const requests = patches(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.url).toBe(`${indicatorsUrl}/widgets/w1`);
      expect(requests[0].request.body).toEqual({ layout: { x: 0, y: 3, w: 6, h: 4 } });
      requests[0].flush(widget('w1', { layout: { x: 0, y: 3, w: 6, h: 4 } }));
      await settle(context.harness);

      await wait(layoutSaveDelayMs + 100);
      expect(patches(context)).toHaveLength(0);
    }, 10000);

    it('saves a resize with one patch of the layout', async () => {
      const context = await open('analyst', [left], rows);

      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowDown');
      await pressAndSettle(context, handle(context, 'w1', 'resize'), 'ArrowRight');
      await wait(layoutSaveDelayMs + 100);

      const requests = patches(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body).toEqual({ layout: { x: 0, y: 0, w: 7, h: 5 } });
      requests[0].flush(left);
    }, 10000);

    it('saves the widgets a move pushed down too, each once', async () => {
      const context = await open('analyst', [left, right], rows);

      for (let i = 0; i < 6; i++) {
        await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowRight');
      }
      await wait(layoutSaveDelayMs + 100);

      const requests = patches(context);
      const byWidget = Object.fromEntries(requests.map((r) => [r.request.url.split('/').pop(), r.request.body.layout as Layout]));
      expect(requests).toHaveLength(2);
      expect(byWidget['w1']).toEqual({ x: 6, y: 0, w: 6, h: 4 });
      expect(byWidget['w2']).toEqual({ x: 6, y: 4, w: 6, h: 4 });
      requests.forEach((r) => r.flush(left));
    }, 10000);

    it('does not ask for the widget\'s rows again after a move', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await wait(layoutSaveDelayMs + 100);

      expect(context.http.match((r) => r.method === 'POST' && r.url.endsWith('/query'))).toHaveLength(0);
      patches(context).forEach((r) => r.flush(left));
    }, 10000);

    it('shows what the server has when a save fails', async () => {
      const context = await open('analyst', [left, right], rows);

      await pressAndSettle(context, handle(context, 'w1', 'move'), 'ArrowDown');
      await wait(layoutSaveDelayMs + 100);
      patches(context)[0].flush({ title: 'x' }, { status: 403, statusText: 'Forbidden' });
      await settle(context.harness);

      const reload = context.http.expectOne((r) => r.method === 'GET' && r.url === indicatorsUrl);
      reload.flush({ published: true, publishedAt: null, runId: null, widgets: [left, right], limits: { maxWidgets: 50, maxQueryRows: 5000 } });
      await settle(context.harness);
      await answerQueries(context, rows);
      expect(place(context, 'w1')).toEqual({ column: '1 / span 6', row: '1 / span 4' });
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
      const requests = patches(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body).toEqual({ layout: { x: 0, y: 4, w: 6, h: 4 } });
      requests[0].flush(left);
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
      const requests = patches(context);
      expect(requests).toHaveLength(1);
      expect(requests[0].request.body).toEqual({ layout: { x: 0, y: 0, w: 8, h: 5 } });
      requests[0].flush(left);
    }, 10000);
  });
});
