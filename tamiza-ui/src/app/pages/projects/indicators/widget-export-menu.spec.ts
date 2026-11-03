import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { tick } from '../../../../testing/cdk';
import { configureApp, settle } from '../../../../testing/project-harness';
import { WidgetFileExtension } from './widget-export/file-name';
import { Context, all, clearOverlay, indicator, isQuery, open, paperOn, result, row, text, widget } from './indicators-testing';
import { WidgetExportChoice, WidgetGrid } from './widget-grid';

const rows = result([row('Cali', 75)]);
const coverage = widget('w1', { title: 'Coverage by municipality', layout: { x: 0, y: 0, w: 6, h: 4 } });
const water = widget('w2', { title: 'Type of dwelling', chartType: 'pie', layout: { x: 6, y: 0, w: 6, h: 4 } });

const grid = (context: Context) => context.harness.fixture.debugElement.query(By.directive(WidgetGrid));
const menuItems = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"]'));
const triggerOf = (context: Context, index = 0) => all(context.root, 'widget-export')[index];

async function openMenu(context: Context, index = 0) {
  triggerOf(context, index).click();
  await settle(context.harness);
  return menuItems();
}

// Every member exports the widgets of a project's own dashboards (add-widget-export: Export button, Formats that cannot be exported yet).
describe('the Export button of a widget', () => {
  afterEach(clearOverlay);

  it.each(['viewer', 'analyst', 'admin'] as const)('is offered to a %s on every widget, named after it', async (role) => {
    const context = await open(role, [coverage, water], rows);

    expect(all(context.root, 'widget-export')).toHaveLength(2);
    expect(triggerOf(context, 0).getAttribute('aria-label')).toBe('Export “Coverage by municipality”');
    expect(triggerOf(context, 1).getAttribute('aria-label')).toBe('Export “Type of dwelling”');
  });

  it('comes after the table toggle and before the actions menu, which keeps only Edit and Remove', async () => {
    const context = await open('analyst', [coverage], rows);
    const header = triggerOf(context).closest('header')!;

    const order = Array.from(header.querySelectorAll<HTMLElement>('[data-testid="widget-title"], [data-testid="widget-table-toggle"], [data-testid="widget-export"], [data-testid="menu-trigger"]')).map((e) => e.dataset['testid']);
    expect(order).toEqual(['widget-title', 'widget-table-toggle', 'widget-export', 'menu-trigger']);

    all(context.root, 'menu-trigger')[0].click();
    await settle(context.harness);
    expect(menuItems().map((item) => text(item))).toEqual(['Edit widget', 'Remove widget']);
  });

  it('gives a viewer the Export button and none of the editing controls', async () => {
    const context = await open('viewer', [coverage], rows);

    expect(all(context.root, 'widget-export')).toHaveLength(1);
    expect(all(context.root, 'menu-trigger')).toHaveLength(0);
    expect(all(context.root, 'widget-move')).toHaveLength(0);
  });

  it('offers a PNG image, an SVG image and an Excel workbook, all available for a widget with rows', async () => {
    const context = await open('viewer', [coverage], rows);

    const items = await openMenu(context);

    expect(items.map((item) => item.dataset['testid'])).toEqual(['widget-export-png', 'widget-export-svg', 'widget-export-xlsx']);
    expect(items.map((item) => text(item))).toEqual(['PNG image', 'SVG image', 'Excel workbook (XLSX)']);
    expect(items.map((item) => item.getAttribute('aria-disabled'))).toEqual([null, null, null]);
  });

  it('keeps every format in the menu, announced as unavailable with the reason, while the widget is loading', async () => {
    const context = await open('viewer', [coverage], null);

    const items = await openMenu(context);

    expect(items.map((item) => item.getAttribute('aria-disabled'))).toEqual(['true', 'true', 'true']);
    expect(items.map((item) => text(item.querySelector('[data-testid="menu-item-description"]')))).toEqual(Array(3).fill('The widget is still loading.'));
  });

  it('says why when no row matches, for the images and the workbook alike', async () => {
    const context = await open('viewer', [coverage], result([]));

    const items = await openMenu(context);

    expect(items.map((item) => text(item.querySelector('[data-testid="menu-item-description"]')))).toEqual(Array(3).fill('No rows match this widget’s query.'));
  });

  it('says the indicator has not published, without asking for rows', async () => {
    const context = await open('viewer', [coverage], null, { indicators: [indicator('coverage', { published: false })] });

    const items = await openMenu(context);

    expect(items.map((item) => item.getAttribute('aria-disabled'))).toEqual(['true', 'true', 'true']);
    expect(items.map((item) => text(item.querySelector('[data-testid="menu-item-description"]')))).toEqual(Array(3).fill('Coverage has not published yet.'));
    expect(context.http.match(isQuery)).toHaveLength(0);
  });

  it('reports the format chosen with the widget it belongs to, and nothing for a disabled format', async () => {
    const context = await open('viewer', [coverage, water], rows);
    const chosen: WidgetExportChoice[] = [];
    (grid(context).componentInstance as WidgetGrid).exportWidget.subscribe((choice) => chosen.push(choice));

    (await openMenu(context, 1))[2].click();
    await settle(context.harness);

    expect(chosen.map((c) => [c.widget.id, c.format as WidgetFileExtension])).toEqual([['w2', 'xlsx']]);
    expect(chosen[0].view.exportState().workbook).toBeNull();
  });

  it('is not printed (Content of a printed dashboard)', async () => {
    const context = await open('viewer', [coverage], rows);
    await paperOn(context);

    expect(triggerOf(context).closest('tmz-dropdown-menu')!.classList).toContain('paper:hidden');
  });

  it('is a header control like the others, with no breakpoint that would hide it on a phone', async () => {
    const context = await open('analyst', [coverage, water], rows);

    const classes = Array.from(triggerOf(context).closest('tmz-dropdown-menu')!.classList);
    expect(classes).not.toContain('hidden');
    expect(classes.filter((c) => /^(sm|md|lg|xl):/.test(c))).toEqual([]);
  });
});

@Component({
  imports: [WidgetGrid],
  template: `<app-widget-grid projectId="p1" [widgets]="widgets" [exporting]="exporting()" />`,
})
class Host {
  readonly widgets = [coverage, water];
  readonly exporting = signal<Record<string, WidgetFileExtension>>({});
}

describe('the Export button of a widget while its file is being prepared', () => {
  afterEach(clearOverlay);

  async function render() {
    const http = configureApp();
    const fixture = TestBed.createComponent(Host);
    const run = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await run();
    http.match(isQuery).forEach((r) => r.flush(rows));
    await run();
    const triggers = () => Array.from(fixture.nativeElement.querySelectorAll('[data-testid="widget-export"]')) as HTMLButtonElement[];
    return { fixture, run, triggers };
  }

  it('shows that only that widget is busy, and does not open', async () => {
    const { fixture, run, triggers } = await render();

    fixture.componentInstance.exporting.set({ w1: 'xlsx' });
    await run();

    const [busy, idle] = triggers();
    expect(busy.getAttribute('aria-busy')).toBe('true');
    expect(busy.getAttribute('aria-label')).toBe('Preparing the export of “Coverage by municipality”…');
    expect(idle.getAttribute('aria-busy')).toBeNull();
    busy.click();
    await run();
    expect(menuItems()).toHaveLength(0);
    idle.click();
    await run();
    expect(menuItems()).toHaveLength(3);
  });

  it('opens again once the export ends', async () => {
    const { fixture, run, triggers } = await render();
    fixture.componentInstance.exporting.set({ w1: 'png' });
    await run();

    fixture.componentInstance.exporting.set({});
    await run();

    expect(triggers()[0].getAttribute('aria-busy')).toBeNull();
    expect(triggers()[0].getAttribute('aria-label')).toBe('Export “Coverage by municipality”');
  });
});
