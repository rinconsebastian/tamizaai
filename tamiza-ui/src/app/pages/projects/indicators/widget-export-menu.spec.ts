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
const menuEntries = () => Array.from(document.querySelectorAll<HTMLElement>('[role="menuitem"], [role="menuitemcheckbox"]'));
const formats = () => menuEntries().filter((entry) => entry.dataset['testid']?.startsWith('widget-export-'));
const triggerOf = (context: Context, index = 0) => all(context.root, 'widget-menu')[index];

async function openMenu(context: Context, index = 0) {
  triggerOf(context, index).click();
  await settle(context.harness);
  return formats();
}

// Every member exports the widgets of a project's own dashboards, from the Export group of the widget's menu
// (widget-export: Export in the widget menu, Formats that cannot be exported yet).
describe('the Export group of a widget menu', () => {
  afterEach(clearOverlay);

  it.each(['viewer', 'analyst', 'admin'] as const)('is offered to a %s on every widget, from one menu named after it', async (role) => {
    const context = await open(role, [coverage, water], rows);

    expect(all(context.root, 'widget-menu')).toHaveLength(2);
    expect(triggerOf(context, 0).getAttribute('aria-label')).toBe('Options for “Coverage by municipality”');
    expect(triggerOf(context, 1).getAttribute('aria-label')).toBe('Options for “Type of dwelling”');
  });

  it('leaves the header with the move handle, the title and the menu: no other button (Analyst’s widget header)', async () => {
    const context = await open('analyst', [coverage], rows);
    const header = triggerOf(context).closest('header')!;

    const order = Array.from(header.querySelectorAll<HTMLElement>('button, h3')).map((e) => e.dataset['testid']);
    expect(order).toEqual(['widget-move', 'widget-title', 'widget-menu']);
    expect(header.querySelector('[data-testid="widget-table-toggle"]')).toBeNull();
    expect(header.querySelector('[data-testid="widget-export"]')).toBeNull();
    expect(header.querySelector('[data-testid="menu-trigger"]')).toBeNull();
  });

  it('is a labelled group of items of the menu itself, between Show as table and the editing actions (Formats in the menu itself)', async () => {
    const context = await open('analyst', [coverage], rows);
    triggerOf(context).click();
    await settle(context.harness);

    expect(menuEntries().map((entry) => text(entry))).toEqual(['Show as table', 'PNG image', 'SVG image', 'Excel workbook (XLSX)', 'Edit widget', 'Remove widget']);
    expect(menuEntries().map((entry) => entry.getAttribute('role'))).toEqual(['menuitemcheckbox', 'menuitem', 'menuitem', 'menuitem', 'menuitem', 'menuitem']);
    const groups = Array.from(document.querySelectorAll('[role="menu"] [role="group"]'));
    expect(groups.map((group) => group.getAttribute('aria-label'))).toEqual([null, 'Export', null]);
    expect(groups[1].querySelectorAll('[role="menuitem"]')).toHaveLength(3);
    expect(document.querySelector('[role="menu"] [aria-haspopup]')).toBeNull(); // no submenu
    expect(document.querySelectorAll('[role="menu"] [role="separator"]')).toHaveLength(2);
  });

  it('gives a viewer Show as table and the formats, and none of the editing controls', async () => {
    const context = await open('viewer', [coverage], rows);
    triggerOf(context).click();
    await settle(context.harness);

    expect(menuEntries().map((entry) => text(entry))).toEqual(['Show as table', 'PNG image', 'SVG image', 'Excel workbook (XLSX)']);
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

  it('is a header control like the others, with no breakpoint that would hide it on a phone (Phone width)', async () => {
    const context = await open('analyst', [coverage, water], rows);

    const classes = Array.from(triggerOf(context).closest('tmz-dropdown-menu')!.classList);
    expect(classes).not.toContain('hidden');
    expect(classes.filter((c) => /^(sm|md|lg|xl):/.test(c))).toEqual([]);
  });
});

@Component({
  imports: [WidgetGrid],
  template: `<app-widget-grid projectId="p1" [widgets]="widgets" [exporting]="exporting()" [editable]="true" />`,
})
class Host {
  readonly widgets = [coverage, water];
  readonly exporting = signal<Record<string, WidgetFileExtension>>({});
}

describe('the menu of a widget while its file is being prepared', () => {
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
    const triggers = () => Array.from(fixture.nativeElement.querySelectorAll('[data-testid="widget-menu"]')) as HTMLButtonElement[];
    return { fixture, run, triggers };
  }

  it('shows that only that widget is busy, and still opens with its formats unavailable (Two widgets, Menu of a widget being exported)', async () => {
    const { fixture, run, triggers } = await render();

    fixture.componentInstance.exporting.set({ w1: 'xlsx' });
    await run();

    const [busy, idle] = triggers();
    expect(busy.getAttribute('aria-busy')).toBe('true');
    expect(busy.getAttribute('aria-label')).toBe('Options for “Coverage by municipality”, preparing its export');
    expect(idle.getAttribute('aria-busy')).toBeNull();

    busy.click();
    await run();
    expect(formats().map((item) => item.getAttribute('aria-disabled'))).toEqual(['true', 'true', 'true']);
    expect(formats().map((item) => text(item.querySelector('[data-testid="menu-item-description"]')))).toEqual(Array(3).fill('Preparing the export…'));
    const others = menuEntries().filter((entry) => !formats().includes(entry));
    expect(others.map((entry) => entry.getAttribute('aria-disabled'))).toEqual([null, null, null]); // Show as table, Edit, Remove
    expect(others.map((entry) => text(entry))).toEqual(['Show as table', 'Edit widget', 'Remove widget']);
    clearOverlay();

    idle.click();
    await run();
    expect(formats().map((item) => item.getAttribute('aria-disabled'))).toEqual([null, null, null]);
  });

  it('keeps Show as table usable while the export runs', async () => {
    const { fixture, run, triggers } = await render();
    fixture.componentInstance.exporting.set({ w1: 'png' });
    await run();

    const tableOf = (index: number) => fixture.nativeElement.querySelectorAll('app-widget-view')[index].querySelector('[data-testid="widget-table"]');
    expect(tableOf(0)).toBeNull();
    triggers()[0].click();
    await run();
    const table = menuEntries().find((entry) => entry.dataset['testid'] === 'widget-show-table')!;
    expect(table.getAttribute('aria-disabled')).toBeNull();
    table.click();
    await run();

    expect(tableOf(0)).not.toBeNull();
    expect(tableOf(1)).toBeNull(); // the other widget keeps its chart
    expect(fixture.nativeElement.querySelector('[data-testid="grid-status"]').textContent).toContain('“Coverage by municipality” is shown as a table');
  });

  it('has everything available again once the export ends (Export ends)', async () => {
    const { fixture, run, triggers } = await render();
    fixture.componentInstance.exporting.set({ w1: 'png' });
    await run();

    fixture.componentInstance.exporting.set({});
    await run();

    expect(triggers()[0].getAttribute('aria-busy')).toBeNull();
    expect(triggers()[0].getAttribute('aria-label')).toBe('Options for “Coverage by municipality”');
    triggers()[0].click();
    await run();
    expect(formats().map((item) => item.getAttribute('aria-disabled'))).toEqual([null, null, null]);
  });
});
