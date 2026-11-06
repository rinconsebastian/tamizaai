import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DashboardChange, DashboardVersionList, DashboardVersionSummary, dashboardChangeKinds } from '../../../core/projects/indicators.service';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { dateText } from '../../../shared/ui';
import { provideVisibleElements, tick } from '../../../../testing/cdk';
import { localeTesting, translocoTesting } from '../../../../testing/transloco-testing';
import { DashboardVersionsData, DashboardVersionsDialog, DashboardVersionsResult } from './dashboard-versions-dialog';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const versionsUrl = '/api/v1/projects/p1/dashboards/d1/versions';
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
/** ICU versions differ in which space they put before "AM"; compare with plain spaces. */
const plain = (value: string) => value.replace(/[  ]/g, ' ');
/** A time as the shared `dateTime` pipe writes it in this browser's zone. */
const when = (value: string, locale = 'en-US') => plain(dateText(value, locale)!);
const notFound = [{ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' }] as const;

const ana = { id: 'u1', name: 'Ana Pérez' };
const luis = { id: 'u2', name: 'Luis Gómez' };
/** Version `n` of "Housing" was saved on Oct 31, 2026, `n` minutes past 10:00 UTC. */
const savedAt = (n: number) => `2026-10-31T10:${String(n).padStart(2, '0')}:00Z`;

/** Version `n` of "Housing", made by Ana in Tamiza, saying one widget was changed. */
function version(n: number, overrides: Partial<DashboardVersionSummary> = {}): DashboardVersionSummary {
  return {
    number: n,
    current: false,
    name: 'Housing',
    widgetCount: 3,
    filterCount: 1,
    changes: [{ kind: 'widgetChanged', widget: `Widget ${n}` }],
    moreChanges: 0,
    restoredFrom: null,
    origin: 'app',
    note: null,
    author: ana,
    createdAt: savedAt(n),
    savedAt: savedAt(n),
    ...overrides,
  };
}

/** The versions as the API lists them: newest first, the first one current. */
function listOf(versions: DashboardVersionSummary[]): DashboardVersionList {
  return { current: versions[0]?.number ?? 0, versions: versions.map((v, i) => ({ ...v, current: i === 0 })) };
}

/** "Housing" keeps versions 3 to 12. */
const tenKept = () => listOf(Array.from({ length: 10 }, (_, i) => version(12 - i, { author: i % 2 ? luis : ana })));

describe('DashboardVersionsDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  /** Opens the history of "Housing" (`d1`), or of a dashboard of another name, and, unless `answer` is null, answers its versions. */
  async function open(answer: DashboardVersionList | null = tenKept(), { lang = 'en', locale = 'en-US', name = 'Housing' }: { lang?: InterfaceLanguage; locale?: string; name?: string } = {}) {
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements(), localeTesting(locale)],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const ref = TestBed.inject(Dialog).open<DashboardVersionsResult, DashboardVersionsData>(DashboardVersionsDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', dashboard: { id: 'd1', name } },
    });
    const results: (DashboardVersionsResult | undefined)[] = [];
    let closed = false;
    ref.closed.subscribe((result) => {
      closed = true;
      results.push(result);
    });
    const settle = async () => {
      for (let i = 0; i < 3; i++) {
        fixture.detectChanges();
        await fixture.whenStable();
        await tick();
      }
    };
    await settle();
    const read = http.expectOne((r) => r.method === 'GET' && r.url === versionsUrl);
    if (answer) {
      read.flush(answer);
      await settle();
    }
    const root = () => document.querySelector('[data-testid="dashboard-versions-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const items = () => Array.from(root()?.querySelectorAll<HTMLElement>('[data-testid="dashboard-version"]') ?? []);
    /** The entry of version `n`. */
    const entry = (n: number) => items().find((item) => item.dataset['number'] === String(n)) ?? null;
    const within = (n: number, id: string) => entry(n)?.querySelector<HTMLElement>(`[data-testid="${id}"]`) ?? null;
    const changes = (n: number) => Array.from(entry(n)?.querySelectorAll('[data-testid="dashboard-version-change"]') ?? []).map(text);
    const click = async (element: HTMLElement) => {
      element.click();
      await settle();
    };
    return { http, read, root, get, items, entry, within, changes, click, settle, results, isClosed: () => closed };
  }

  it('lists the kept versions newest first, marks the current one, and shows what changed, who and when for each (History listed)', async () => {
    const dialog = await open();

    expect(text(dialog.root()!.querySelector('h2'))).toBe('Version history of Housing');
    expect(dialog.root()!.querySelector('ol')).not.toBeNull();
    expect(dialog.items().map((item) => text(item.querySelector('[data-testid="dashboard-version-number"]')))).toEqual(
      [12, 11, 10, 9, 8, 7, 6, 5, 4, 3].map((n) => `Version ${n}`),
    );
    expect(text(dialog.within(12, 'dashboard-version-current'))).toBe('Current');
    expect(dialog.items().filter((item) => item.querySelector('[data-testid="dashboard-version-current"]'))).toHaveLength(1);
    for (const n of [12, 11, 10, 9, 8, 7, 6, 5, 4, 3]) {
      expect(dialog.changes(n)).toEqual([`Widget “Widget ${n}” changed`]);
      expect(plain(text(dialog.within(n, 'dashboard-version-byline')))).toBe(`${n % 2 ? 'Luis Gómez' : 'Ana Pérez'} · ${when(savedAt(n))}`);
      expect(dialog.within(n, 'dashboard-version-via-mcp')).toBeNull();
    }
    expect(dialog.get('dashboard-versions-loading')).toBeNull();
  });

  it('gives the time the version\'s last change was saved, not when it began', async () => {
    const dialog = await open(listOf([version(5, { createdAt: '2026-10-30T08:00:00Z', savedAt: '2026-10-31T15:32:00Z' })]));

    expect(plain(text(dialog.within(5, 'dashboard-version-byline')))).toBe(`Ana Pérez · ${when('2026-10-31T15:32:00Z')}`);
  });

  it('shows the version Claude made with the request in quotes, "via MCP", its first three changes and "and 2 more" (Version made by Claude)', async () => {
    const added = ['Water source', 'Piped water', 'Water treatment', 'Distance to water', 'Water cost'].map((widget): DashboardChange => ({ kind: 'widgetAdded', widget }));
    const dialog = await open(listOf([version(13, { origin: 'mcp', note: 'Add five water indicators', changes: added }), version(12)]));

    expect(text(dialog.within(13, 'dashboard-version-note'))).toBe('“Add five water indicators”');
    expect(dialog.changes(13)).toEqual(['Widget “Water source” added', 'Widget “Piped water” added', 'Widget “Water treatment” added']);
    expect(text(dialog.within(13, 'dashboard-version-more'))).toBe('and 2 more');
    expect(plain(text(dialog.within(13, 'dashboard-version-byline')))).toBe(`Ana Pérez · ${when(savedAt(13))} · via MCP`);
    expect(text(dialog.within(13, 'dashboard-version-via-mcp'))).toBe('via MCP');
    // One made in Tamiza has neither.
    expect(dialog.within(12, 'dashboard-version-note')).toBeNull();
    expect(dialog.within(12, 'dashboard-version-more')).toBeNull();
  });

  it('shows the first changes and how many more there are, those the API left out included (Several changes in one version)', async () => {
    const twenty = Array.from({ length: 20 }, (_, i): DashboardChange => ({ kind: 'widgetChanged', widget: `Widget ${i + 1}` }));
    const dialog = await open(listOf([version(9, { changes: twenty, moreChanges: 4 }), version(8, { changes: twenty.slice(0, 4) }), version(7, { changes: twenty.slice(0, 3) })]));

    expect(dialog.changes(9)).toEqual(['Widget “Widget 1” changed', 'Widget “Widget 2” changed', 'Widget “Widget 3” changed']);
    expect(text(dialog.within(9, 'dashboard-version-more'))).toBe('and 21 more');
    expect(text(dialog.within(8, 'dashboard-version-more'))).toBe('and 1 more');
    expect(dialog.changes(7)).toHaveLength(3);
    expect(dialog.within(7, 'dashboard-version-more')).toBeNull();
  });

  it('says what each kind of change was, with the names and numbers it carries', async () => {
    const kinds: [DashboardChange, string][] = [
      [{ kind: 'initial' }, 'State before version history'],
      [{ kind: 'created' }, 'Dashboard added'],
      [{ kind: 'duplicated', dashboard: 'Health' }, 'Duplicated from “Health”'],
      [{ kind: 'renamed', dashboard: 'Agua' }, 'Renamed to “Agua”'],
      [{ kind: 'filtersChanged' }, 'Filters changed'],
      [{ kind: 'colorsChanged' }, 'Colors changed'],
      [{ kind: 'sourceChanged' }, 'Source changed'],
      [{ kind: 'widgetAdded', widget: 'Water source' }, 'Widget “Water source” added'],
      [{ kind: 'widgetChanged', widget: 'Coverage.' }, 'Widget “Coverage.” changed'],
      [{ kind: 'widgetRemoved', widget: 'Water source' }, 'Widget “Water source” removed'],
      [{ kind: 'widgetMovedOut', widget: 'Coverage', dashboard: 'Field' }, 'Widget “Coverage” moved to “Field”'],
      [{ kind: 'widgetMovedIn', widget: 'Coverage', dashboard: 'Health' }, 'Widget “Coverage” moved here from “Health”'],
      [{ kind: 'layoutChanged' }, 'Widgets moved or resized'],
      [{ kind: 'restored', version: 7 }, 'Version 7 restored'],
      [{ kind: 'unrecorded' }, 'Changes kept before a restore'],
    ];
    const dialog = await open(listOf(kinds.map(([change], i) => version(kinds.length - i, { changes: [change] }))));

    expect(dialog.items().map((item) => text(item.querySelector('[data-testid="dashboard-version-change"]')))).toEqual(kinds.map(([, words]) => words));
  });

  it.each<[InterfaceLanguage, string]>([
    ['en', 'Colors changed'],
    ['es', 'Se cambiaron los colores'],
  ])('says in %s that a version changed the dashboard\'s colors, its palette or the colors kept by value (Palette changed)', async (lang, words) => {
    // Alone (version 5), and among the other changes a version holds (version 3).
    const dialog = await open(
      listOf([
        version(5, { changes: [{ kind: 'colorsChanged' }] }),
        version(3, { changes: [{ kind: 'filtersChanged' }, { kind: 'colorsChanged' }, { kind: 'widgetAdded', widget: 'Water' }] }),
      ]),
      { lang },
    );

    expect(dialog.changes(5)).toEqual([words]);
    expect(dialog.changes(3)[1]).toBe(words);
  });

  it.each<[InterfaceLanguage, string]>([
    ['en', 'Source changed'],
    ['es', 'Se cambió la fuente'],
  ])('says in %s that a version changed the source of the dashboard', async (lang, words) => {
    const dialog = await open(listOf([version(5, { changes: [{ kind: 'sourceChanged' }] })]), { lang });

    expect(dialog.changes(5)).toEqual([words]);
  });

  it.each<[InterfaceLanguage, string]>([
    ['en', 'Base sizes changed'],
    ['es', 'Se cambiaron los tamaños de base'],
  ])('says in %s that a version changed the base settings of the dashboard', async (lang, words) => {
    const dialog = await open(listOf([version(5, { changes: [{ kind: 'basesChanged' }] })]), { lang });

    expect(dialog.changes(5)).toEqual([words]);
  });

  it.each<InterfaceLanguage>(['en', 'es'])('has words in %s for every kind of change the service lists', async (lang) => {
    const dialog = await open(
      listOf(dashboardChangeKinds.map((kind, i) => version(dashboardChangeKinds.length - i, { changes: [{ kind, widget: 'W', dashboard: 'D', version: 7 }] }))),
      { lang },
    );

    for (const item of dialog.items()) {
      const words = text(item.querySelector('[data-testid="dashboard-version-change"]'));
      expect(words).not.toBe('');
      expect(words).not.toContain('dashboards.versions.changes');
    }
    expect(dialog.items()).toHaveLength(dashboardChangeKinds.length);
  });

  it('writes the history in Spanish, in the interface\'s regional format', async () => {
    const dialog = await open(
      listOf([
        version(13, { origin: 'mcp', note: 'Agrega cinco indicadores de agua', changes: ['Fuente', 'Tubería', 'Tratamiento', 'Distancia'].map((widget): DashboardChange => ({ kind: 'widgetAdded', widget })) }),
        version(12, { changes: [{ kind: 'widgetMovedOut', widget: 'Cobertura', dashboard: 'Campo' }, { kind: 'restored', version: 7 }] }),
        version(1, { changes: [{ kind: 'initial' }] }),
      ]),
      { lang: 'es', locale: 'es-CO', name: 'Vivienda' },
    );

    expect(text(dialog.root()!.querySelector('h2'))).toBe('Historial de versiones de Vivienda');
    expect(text(dialog.within(13, 'dashboard-version-number'))).toBe('Versión 13');
    expect(text(dialog.within(13, 'dashboard-version-current'))).toBe('Actual');
    expect(dialog.changes(13)).toEqual(['Se agregó el widget “Fuente”', 'Se agregó el widget “Tubería”', 'Se agregó el widget “Tratamiento”']);
    expect(text(dialog.within(13, 'dashboard-version-more'))).toBe('y 1 más');
    expect(plain(text(dialog.within(13, 'dashboard-version-byline')))).toBe(`Ana Pérez · ${when(savedAt(13), 'es-CO')} · vía MCP`);
    expect(dialog.changes(12)).toEqual(['El widget “Cobertura” pasó a “Campo”', 'Se restauró la versión 7']);
    expect(dialog.changes(1)).toEqual(['Estado antes del historial de versiones']);
    expect(text(dialog.within(12, 'dashboard-version-view'))).toBe('Ver');
    expect(dialog.within(12, 'dashboard-version-restore')!.getAttribute('aria-label')).toBe('Restaurar la versión 12');
  });

  it('shows names, titles and requests holding markup as text', async () => {
    const dialog = await open(
      listOf([
        version(4, { origin: 'mcp', note: '<i>Add</i> a chart', changes: [{ kind: 'widgetAdded', widget: '<b>Water</b>' }, { kind: 'renamed', dashboard: '<img src=x>' }] }),
        version(3),
      ]),
      { name: '<em>Housing</em>' },
    );

    expect(dialog.changes(4)).toEqual(['Widget “<b>Water</b>” added', 'Renamed to “<img src=x>”']);
    expect(text(dialog.within(4, 'dashboard-version-note'))).toBe('“<i>Add</i> a chart”');
    expect(text(dialog.root()!.querySelector('h2'))).toBe('Version history of <em>Housing</em>');
    expect(dialog.root()!.querySelector('b, i, em, img')).toBeNull();
  });

  it('offers View and Restore on every version but the current one, each naming its version', async () => {
    const dialog = await open();

    expect(dialog.within(12, 'dashboard-version-actions')).toBeNull();
    for (const n of [11, 10, 9, 8, 7, 6, 5, 4, 3]) {
      const view = dialog.within(n, 'dashboard-version-view')!;
      const restore = dialog.within(n, 'dashboard-version-restore')!;
      expect(text(view)).toBe('View');
      expect(view.getAttribute('aria-label')).toBe(`View version ${n}`);
      expect(text(restore)).toBe('Restore');
      expect(restore.getAttribute('aria-label')).toBe(`Restore version ${n}`);
    }
  });

  it('closes with the version to view', async () => {
    const dialog = await open();

    await dialog.click(dialog.within(9, 'dashboard-version-view')!);

    expect(dialog.results).toEqual([{ view: 9 }]);
  });

  it('closes with the version to restore, the name it holds and the time it was saved, restoring nothing itself', async () => {
    const dialog = await open(listOf([version(8), version(7, { name: 'Water' })]));

    await dialog.click(dialog.within(7, 'dashboard-version-restore')!);

    expect(dialog.results).toEqual([{ restore: { number: 7, name: 'Water', savedAt: savedAt(7) } }]);
    dialog.http.expectNone((r) => r.method === 'POST');
  });

  it('closes with nothing from Close, which has the focus when it opens', async () => {
    const dialog = await open();
    const close = dialog.get('dashboard-versions-close')!;
    expect(text(close)).toBe('Close');
    expect(document.activeElement).toBe(close);

    await dialog.click(close);

    expect(dialog.results).toEqual([undefined]);
  });

  it('shows that the versions are being read, with nothing to choose', async () => {
    const dialog = await open(null);

    expect(dialog.get('dashboard-versions-loading')!.getAttribute('aria-busy')).toBe('true');
    expect(text(dialog.get('dashboard-versions-loading'))).toBe('Reading the versions…');
    expect(dialog.items()).toHaveLength(0);
    expect(dialog.get('dashboard-versions-close')).not.toBeNull();

    dialog.read.flush(tenKept());
    await dialog.settle();
    expect(dialog.get('dashboard-versions-loading')).toBeNull();
    expect(dialog.items()).toHaveLength(10);
  });

  it('says why the versions could not be read, with nothing to choose, and stays open', async () => {
    const dialog = await open(null);

    dialog.read.flush(null, { status: 500, statusText: 'Server Error' });
    await dialog.settle();

    expect(text(dialog.get('dashboard-versions-error'))).toBe('Something went wrong. Try again.');
    expect(dialog.get('dashboard-versions-error')!.getAttribute('role')).toBe('alert');
    expect(dialog.get('dashboard-versions-loading')).toBeNull();
    expect(dialog.items()).toHaveLength(0);
    expect(dialog.get('dashboard-versions-close')).not.toBeNull();
    expect(dialog.isClosed()).toBe(false);
  });

  it('closes with gone when the dashboard was removed before its versions were read', async () => {
    const dialog = await open(null);

    dialog.read.flush(...notFound);
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
  });

  it('says so when the dashboard has no version yet', async () => {
    const dialog = await open({ current: 0, versions: [] });

    expect(text(dialog.get('dashboard-versions-empty'))).toBe('No versions recorded yet. The next change saved to this dashboard makes its first version.');
    expect(dialog.root()!.querySelector('ol')).toBeNull();
  });

  it('keeps every version\'s number, changes, author, time and actions within a narrow screen (History at phone width)', async () => {
    // jsdom lays nothing out, so this checks what keeps the dialog inside a 360-pixel viewport; the stack check measures it.
    const long = 'x'.repeat(40);
    const dialog = await open(
      listOf([
        version(12, { changes: [{ kind: 'renamed', dashboard: long }] }),
        version(11, { origin: 'mcp', note: 'y'.repeat(120), changes: [{ kind: 'widgetAdded', widget: 'z'.repeat(200) }, { kind: 'widgetMovedIn', widget: 'w'.repeat(200), dashboard: long }] }),
        version(10, { author: { id: 'u3', name: 'v'.repeat(80) } }),
      ]),
      { name: long },
    );

    const root = dialog.root()!;
    // As wide as the screen less a margin, at most 40rem; anything long wraps instead of widening it; the list scrolls inside it.
    expect(root.classList).toContain('w-[min(40rem,calc(100vw-2rem))]');
    expect(root.classList).toContain('max-h-[calc(100dvh-2rem)]');
    expect(root.classList).toContain('[overflow-wrap:anywhere]');
    expect(dialog.get('dashboard-versions-body')!.classList).toContain('overflow-y-auto');
    // The heading gives way to Close, which stays whole.
    expect(root.querySelector('h2')!.parentElement!.classList).toContain('min-w-0');
    expect(dialog.get('dashboard-versions-close')!.classList).toContain('shrink-0');
    for (const item of dialog.items()) {
      expect(item.classList).toContain('min-w-0');
      expect(item.querySelector('[data-testid="dashboard-version-number"]')).not.toBeNull();
      expect(item.querySelector('[data-testid="dashboard-version-change"]')).not.toBeNull();
      expect(text(item.querySelector('[data-testid="dashboard-version-byline"]'))).toContain(' · ');
    }
    // The actions wrap onto another line rather than leave the screen, and none is hidden at any width.
    for (const n of [11, 10]) {
      const actions = dialog.within(n, 'dashboard-version-actions')!;
      expect(actions.classList).toContain('flex-wrap');
      const buttons = Array.from(actions.querySelectorAll<HTMLButtonElement>('button'));
      expect(buttons.map((b) => b.dataset['testid'])).toEqual(['dashboard-version-view', 'dashboard-version-restore']);
      for (const button of buttons) {
        expect(button.closest('.hidden')).toBeNull();
        expect(button.disabled).toBe(false);
        expect(button.tabIndex).toBe(0);
      }
    }
  });
});
