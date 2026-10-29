import { ApplicationRef, EnvironmentInjector, createEnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DashboardPrint, printDeadlineMs, printTitle, printingClass } from './dashboard-print';

// Paper mode (dashboard printing, design D1, D5, D6, D9, D10). jsdom lays nothing out and never prints, so these check the steps,
// their order and the waiting; the sheets themselves are checked in a real browser (tasks, group 7).

const view = document.defaultView!;

function create() {
  const injector = createEnvironmentInjector([DashboardPrint], TestBed.inject(EnvironmentInjector));
  return { print: injector.get(DashboardPrint), injector };
}

/** Fake timers for the wait: its polling, its deadline and the two frames it waits for (where jsdom has frames). */
function fakeTimers() {
  const frames = typeof requestAnimationFrame === 'function' ? (['requestAnimationFrame', 'cancelAnimationFrame'] as const) : [];
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date', ...frames] });
}

describe('DashboardPrint', () => {
  beforeEach(() => {
    document.title = 'Tamiza';
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    document.documentElement.classList.remove(printingClass);
    document.getElementById('tmz-print-page')?.remove();
  });

  it('lays the dashboard out as it prints: the class, the page rule and the title, then measures between two renders', () => {
    const { print } = create();
    const steps: string[] = [];
    vi.spyOn(TestBed.inject(ApplicationRef), 'tick').mockImplementation(() => void steps.push('render'));
    print.registerMeasure(() => steps.push('chart'));
    print.registerMeasure(() => steps.push('map'));
    print.titleSource = () => 'Census 2026 – Housing – 2026-10-28';

    print.prepare();

    expect(document.documentElement.classList).toContain(printingClass);
    expect(document.getElementById('tmz-print-page')?.textContent).toBe('@page { margin: 10mm; }');
    expect(document.title).toBe('Census 2026 – Housing – 2026-10-28');
    expect(steps).toEqual(['render', 'chart', 'map', 'render']);
    expect(print.printing()).toBe(true);
    expect(print.printedAt()).toBeInstanceOf(Date);
  });

  it('measures only what is still registered', () => {
    const { print } = create();
    const measured: string[] = [];
    const off = print.registerMeasure(() => measured.push('gone'));
    print.registerMeasure(() => measured.push('kept'));
    off();

    print.prepare();

    expect(measured).toEqual(['kept']);
  });

  it('undoes every step on restore, once, and says paper mode ended', () => {
    const { print } = create();
    const restored: { fromAction: boolean }[] = [];
    print.restored.subscribe((event) => restored.push(event));
    print.titleSource = () => 'Census 2026 – Housing – 2026-10-28';
    print.prepare();

    print.restore();
    print.restore();

    expect(document.documentElement.classList).not.toContain(printingClass);
    expect(document.getElementById('tmz-print-page')).toBeNull();
    expect(document.title).toBe('Tamiza');
    expect(print.printing()).toBe(false);
    expect(print.printedAt()).toBeNull();
    expect(restored).toEqual([{ fromAction: false }]);
  });

  it('waits while a widget is pending, then resolves once it is not', async () => {
    fakeTimers();
    const { print } = create();
    const loading = signal(true);
    print.registerPending(loading);
    print.prepare();
    let ready: boolean | undefined;
    void print.whenReady().then((value) => (ready = value));

    await vi.advanceTimersByTimeAsync(5000);
    expect(ready).toBeUndefined();

    loading.set(false);
    await vi.advanceTimersByTimeAsync(200);
    expect(ready).toBe(true);
  });

  it('stops waiting at the deadline, so a widget that never loads does not hold the print back', async () => {
    fakeTimers();
    const { print } = create();
    print.registerPending(signal(true));
    print.prepare();
    let ready: boolean | undefined;
    void print.whenReady().then((value) => (ready = value));

    await vi.advanceTimersByTimeAsync(printDeadlineMs - 100);
    expect(ready).toBeUndefined();

    await vi.advanceTimersByTimeAsync(300);
    expect(ready).toBe(true);
  });

  it('answers false when paper mode ends while it waits (the page was left)', async () => {
    fakeTimers();
    const { print } = create();
    print.registerPending(signal(true));
    print.prepare();
    let ready: boolean | undefined;
    void print.whenReady().then((value) => (ready = value));

    print.restore();
    await vi.advanceTimersByTimeAsync(200);

    expect(ready).toBe(false);
  });

  it('prepares on beforeprint, only when not printing yet, and restores on afterprint (the browser\'s own command)', () => {
    const { print } = create();
    const measure = vi.fn();
    print.registerMeasure(measure);

    view.dispatchEvent(new Event('beforeprint'));
    view.dispatchEvent(new Event('beforeprint'));
    expect(print.printing()).toBe(true);
    expect(measure).toHaveBeenCalledTimes(1);

    view.dispatchEvent(new Event('afterprint'));
    expect(print.printing()).toBe(false);
  });

  it('opens the dialog once ready, after `opening`, and restores from the Print action when it closes', async () => {
    const { print } = create();
    const steps: string[] = [];
    const restored: { fromAction: boolean }[] = [];
    print.restored.subscribe((event) => restored.push(event));
    // A browser whose dialog blocks, as Chrome's does: afterprint comes before print() returns.
    vi.spyOn(view, 'print').mockImplementation(() => {
      steps.push(`dialog (printing: ${print.printing()})`);
      view.dispatchEvent(new Event('afterprint'));
    });

    await print.print(() => steps.push('opening'));

    expect(steps).toEqual(['opening', 'dialog (printing: true)']);
    expect(print.printing()).toBe(false);
    expect(restored).toEqual([{ fromAction: true }]);
  });

  it('restores on the first pointer or key on the page when the dialog neither blocks nor says it closed', async () => {
    const { print } = create();
    vi.spyOn(view, 'print').mockImplementation(() => undefined);

    await print.print();
    expect(print.printing()).toBe(true);

    view.dispatchEvent(new Event('keydown'));
    expect(print.printing()).toBe(false);
  });

  it('opens no dialog when paper mode ended while it waited', async () => {
    fakeTimers();
    const { print } = create();
    const dialog = vi.spyOn(view, 'print').mockImplementation(() => undefined);
    print.registerPending(signal(true));

    const done = print.print();
    print.restore();
    await vi.advanceTimersByTimeAsync(200);
    await done;

    expect(dialog).not.toHaveBeenCalled();
  });

  it('restores and stops listening once destroyed', () => {
    const { print, injector } = create();
    print.prepare();

    injector.destroy();

    expect(print.printing()).toBe(false);
    expect(document.documentElement.classList).not.toContain(printingClass);
    view.dispatchEvent(new Event('beforeprint'));
    expect(print.printing()).toBe(false);
  });
});

describe('printTitle', () => {
  it('names the project, the dashboard and the day in the project\'s time zone (PDF file name)', () => {
    const late = new Date('2026-10-29T03:00:00Z');
    expect(printTitle('Census 2026', 'Housing', late, 'America/Bogota')).toBe('Census 2026 – Housing – 2026-10-28');
    expect(printTitle('Census 2026', 'Housing', late, 'UTC')).toBe('Census 2026 – Housing – 2026-10-29');
  });

  it('passes names through as text, and falls back to UTC for a zone it does not know', () => {
    const now = new Date('2026-10-29T03:00:00Z');
    expect(printTitle('A/B', '<b>Avance</b>', now, 'Mars/Olympus')).toBe('A/B – <b>Avance</b> – 2026-10-29');
  });
});
