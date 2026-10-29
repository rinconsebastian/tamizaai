import { Component, ElementRef, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { observeSize } from './chart-data';
import { DashboardPrint, printingClass } from './dashboard-print';

// observeSize on the dashboard page (dashboard printing, design D5): the page prepares the print synchronously, where no
// ResizeObserver fires, so the chart's box is read then. jsdom lays nothing out: the box's rectangle is stubbed.

@Component({ template: `<div #box></div>` })
class Chart {
  private readonly box = viewChild<ElementRef<HTMLElement>>('box');
  readonly size = observeSize(() => this.box());
  element(): HTMLElement {
    return this.box()!.nativeElement;
  }
}

function stubBox(element: HTMLElement, width: number, height: number) {
  return vi.spyOn(element, 'getBoundingClientRect').mockReturnValue({ width, height } as DOMRect);
}

describe('observeSize', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  it('reads the box when the dashboard page prepares the print, and draws for it', async () => {
    TestBed.configureTestingModule({ providers: [DashboardPrint] });
    const fixture = TestBed.createComponent(Chart);
    await fixture.whenStable();
    stubBox(fixture.componentInstance.element(), 640.4, 300.6);

    TestBed.inject(DashboardPrint).prepare();

    expect(fixture.componentInstance.size()).toEqual({ width: 640, height: 301 });
  });

  it('stops reading the box once the chart is gone', async () => {
    TestBed.configureTestingModule({ providers: [DashboardPrint] });
    const fixture = TestBed.createComponent(Chart);
    await fixture.whenStable();
    const box = stubBox(fixture.componentInstance.element(), 640, 300);
    fixture.destroy();

    TestBed.inject(DashboardPrint).prepare();

    expect(box).not.toHaveBeenCalled();
  });

  it('keeps an empty box from blanking the chart', async () => {
    TestBed.configureTestingModule({ providers: [DashboardPrint] });
    const fixture = TestBed.createComponent(Chart);
    await fixture.whenStable();
    stubBox(fixture.componentInstance.element(), 0, 0);

    TestBed.inject(DashboardPrint).prepare();

    expect(fixture.componentInstance.size()).toEqual({ width: 480, height: 280 });
  });

  it('registers nothing where no dashboard page provides paper mode (the widget form\'s preview)', async () => {
    const fixture = TestBed.createComponent(Chart);
    await fixture.whenStable();

    expect(fixture.componentInstance.size()).toEqual({ width: 480, height: 280 });
    expect(TestBed.inject(DashboardPrint, null, { optional: true })).toBeNull();
  });
});
