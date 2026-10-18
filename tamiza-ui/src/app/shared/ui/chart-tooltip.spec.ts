import { Dialog } from '@angular/cdk/dialog';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../testing/cdk';
import { ChartTooltip, ChartTooltipContent, ChartTooltips, tooltipOffset, tooltipPositions } from './chart-tooltip';

@Component({ selector: 'app-probe-dialog', template: '<p data-testid="probe-dialog">A dialog</p>' })
class ProbeDialog {}

const content: ChartTooltipContent = {
  heading: '0042',
  lines: [
    { label: 'Survey progress', value: '45%', align: 'end' },
    { label: 'Dirección', value: 'Cra 5 # 10-20', align: 'start' },
  ],
  footer: 'Combines 2 rows',
};

const panel = () => document.querySelector<HTMLElement>('[data-testid="chart-tooltip"]');
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';

describe('chart tooltip', () => {
  let tooltip: ChartTooltip;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideVisibleElements()] });
    tooltip = TestBed.inject(ChartTooltips).create();
  });

  afterEach(() => {
    tooltip.destroy();
    document.querySelectorAll('.cdk-overlay-container').forEach((container) => container.replaceChildren());
  });

  it('tries four positions at 12 pixels from the point: below right, below left, above right, above left', () => {
    expect(tooltipOffset).toBe(12);
    expect(tooltipPositions.map((p) => [p.overlayX, p.overlayY, p.offsetX, p.offsetY])).toEqual([
      ['start', 'top', 12, 12],
      ['end', 'top', -12, 12],
      ['start', 'bottom', 12, -12],
      ['end', 'bottom', -12, -12],
    ]);
  });

  it('shows the heading, the lines and the footer, each as a label and a value', () => {
    tooltip.open({ x: 100, y: 80 }, content);

    expect(text(panel()!.querySelector('[data-testid="tooltip-heading"]'))).toBe('0042');
    expect(Array.from(panel()!.querySelectorAll('[data-testid="tooltip-line"]')).map(text)).toEqual(['Survey progress: 45%', 'Dirección: Cra 5 # 10-20']);
    expect(text(panel()!.querySelector('[data-testid="tooltip-footer"]'))).toBe('Combines 2 rows');
    expect(panel()!.querySelectorAll('dd')[0].className).toContain('text-end');
    expect(panel()!.querySelectorAll('dd')[1].className).toContain('text-start');
  });

  it('shows only the lines it has: no footer, no lines', () => {
    tooltip.open({ x: 0, y: 0 }, { heading: 'Cali', lines: [], footer: null });

    expect(text(panel())).toBe('Cali');
    expect(panel()!.querySelector('dl')).toBeNull();
    expect(panel()!.querySelector('[data-testid="tooltip-footer"]')).toBeNull();
  });

  it('renders markup in any string as text', () => {
    const hostile = '<img src=x onerror=alert(1)>';

    tooltip.open({ x: 0, y: 0 }, { heading: hostile, lines: [{ label: hostile, value: hostile, align: 'start' }], footer: hostile });

    expect(panel()!.querySelectorAll('img')).toHaveLength(0);
    expect(panel()!.querySelectorAll('*:not(p):not(dl):not(div):not(dt):not(dd)')).toHaveLength(0);
    expect(text(panel()!.querySelector('[data-testid="tooltip-heading"]'))).toBe(hostile);
    expect(text(panel()!.querySelector('dt'))).toBe(`${hostile}:`);
    expect(text(panel()!.querySelector('dd'))).toBe(hostile);
    expect(text(panel()!.querySelector('[data-testid="tooltip-footer"]'))).toBe(hostile);
  });

  it('is detached when closed, not hidden, and attached again when opened', () => {
    tooltip.open({ x: 1, y: 1 }, content);
    expect(tooltip.isOpen).toBe(true);

    tooltip.close();
    expect(tooltip.isOpen).toBe(false);
    expect(panel()).toBeNull();

    tooltip.open({ x: 2, y: 2 }, { ...content, heading: '0043' });
    expect(text(panel()!.querySelector('[data-testid="tooltip-heading"]'))).toBe('0043');
    expect(document.querySelectorAll('[data-testid="chart-tooltip"]')).toHaveLength(1);
  });

  it('changes what it says while it stays open, and follows the point', () => {
    tooltip.open({ x: 10, y: 10 }, content);

    tooltip.open({ x: 40, y: 50 }, { ...content, heading: '0044' });
    tooltip.move({ x: 60, y: 70 });

    expect(document.querySelectorAll('[data-testid="chart-tooltip"]')).toHaveLength(1);
    expect(text(panel()!.querySelector('[data-testid="tooltip-heading"]'))).toBe('0044');
    expect(tooltip.at).toEqual({ x: 60, y: 70 });
  });

  it('is hidden from assistive technology and never under the pointer', () => {
    tooltip.open({ x: 0, y: 0 }, content);

    expect(panel()!.getAttribute('aria-hidden')).toBe('true');
    expect(panel()!.closest<HTMLElement>('.cdk-overlay-pane')!.style.pointerEvents).toBe('none');
  });

  it('closes every tooltip at once when asked to (a tile is being dragged)', () => {
    const other = TestBed.inject(ChartTooltips).create();
    tooltip.open({ x: 1, y: 1 }, content);
    other.open({ x: 2, y: 2 }, content);

    TestBed.inject(ChartTooltips).closeAll();

    expect(tooltip.isOpen).toBe(false);
    expect(other.isOpen).toBe(false);
    other.destroy();
  });

  it('leaves the Escape of a dialog opened beneath it alone: the dialog still closes', async () => {
    const dialog = TestBed.inject(Dialog).open(ProbeDialog);
    let closed = false;
    dialog.closed.subscribe(() => (closed = true));
    await tick();
    tooltip.open({ x: 5, y: 5 }, content);

    pressKey(document.body, 'Escape');
    await tick();

    expect(closed).toBe(true);
    expect(tooltip.isOpen).toBe(true); // closing it is the chart's business
  });
});

