import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { DashboardEntry } from '../../../core/projects/projects.service';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { DashboardNameAction, DashboardNameData, DashboardNameDialog, DashboardNameResult } from './dashboard-name-dialog';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const dashboardsUrl = '/api/v1/projects/p1/dashboards';
const housing: DashboardEntry = { id: 'd1', name: 'Housing' };
const health: DashboardEntry = { id: 'd2', name: 'Health' };

describe('DashboardNameDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(
    action: DashboardNameAction,
    dashboard: DashboardEntry | null = null,
    dashboards: DashboardEntry[] = [housing, health],
    lang: InterfaceLanguage = 'en',
  ) {
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const refused = vi.fn();
    const ref = TestBed.inject(Dialog).open<DashboardNameResult, DashboardNameData>(DashboardNameDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', action, dashboard, dashboards, refused },
    });
    const results: (DashboardNameResult | undefined)[] = [];
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
    const root = () => document.querySelector('[data-testid="dashboard-name-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const input = () => get<HTMLInputElement>('dashboard-name')!;
    const type = async (value: string) => {
      input().value = value;
      input().dispatchEvent(new Event('input'));
      await settle();
    };
    const save = async () => {
      get('dashboard-name-save')!.click();
      await settle();
    };
    const fieldError = () => root()?.querySelector('[data-testid="field-error"]')?.textContent?.trim() ?? null;
    const dialogError = () => get('dashboard-name-error')?.textContent?.trim() ?? null;
    return { http, root, get, input, type, save, settle, fieldError, dialogError, results, refused, isClosed: () => closed };
  }

  it('adds a dashboard under the name typed, trimmed, and closes with it', async () => {
    const dialog = await open('add');
    expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('New dashboard');
    expect(dialog.input().value).toBe('');

    await dialog.type('  Field progress  ');
    await dialog.save();

    const request = dialog.http.expectOne(dashboardsUrl);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Field progress' });
    request.flush({ id: 'd3', name: 'Field progress' });
    await dialog.settle();
    expect(dialog.results).toEqual([{ id: 'd3', name: 'Field progress' }]);
    dialog.http.verify();
  });

  it('asks for a name before sending anything', async () => {
    const dialog = await open('add');

    await dialog.type('   ');
    await dialog.save();

    expect(dialog.fieldError()).toBe('Enter a name.');
    dialog.http.expectNone(dashboardsUrl);
    expect(dialog.isClosed()).toBe(false);
  });

  it('renames a dashboard, starting from its name', async () => {
    const dialog = await open('rename', health);
    expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('Rename Health');
    expect(dialog.input().value).toBe('Health');

    await dialog.type('Salud');
    await dialog.save();

    const request = dialog.http.expectOne(`${dashboardsUrl}/d2`);
    expect(request.request.method).toBe('PATCH');
    expect(request.request.body).toEqual({ name: 'Salud' });
    request.flush({ id: 'd2', name: 'Salud' });
    await dialog.settle();
    expect(dialog.results).toEqual([{ id: 'd2', name: 'Salud' }]);
  });

  it('shows a refused name under the input, keeps what was typed and stays open', async () => {
    const dialog = await open('add');
    await dialog.type('SALUD');
    await dialog.save();

    dialog.http
      .expectOne(dashboardsUrl)
      .flush({ title: 'x', errors: { name: ['Another dashboard of this project is named "Salud".'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.fieldError()).toBe('Another dashboard of this project is named "Salud".');
    expect(dialog.input().getAttribute('aria-invalid')).toBe('true');
    expect(dialog.dialogError()).toBeNull();
    expect(dialog.input().value).toBe('SALUD');
    expect(dialog.isClosed()).toBe(false);
    expect(dialog.refused).not.toHaveBeenCalled();
  });

  it('shows the limit, translated, as the dialog\'s error and tells the page its list is out of date', async () => {
    const dialog = await open('add');
    await dialog.type('Field progress');
    await dialog.save();

    dialog.http.expectOne(dashboardsUrl).flush({ title: 'x', code: 'dashboards.limit' }, { status: 409, statusText: 'Conflict' });
    await dialog.settle();

    expect(dialog.dialogError()).toBe('A project can have at most 7 dashboards. Remove one before adding another.');
    expect(dialog.get('dashboard-name-error')!.querySelector('[lang]')).toBeNull(); // translated from its code: the UI wrote it
    expect(dialog.fieldError()).toBeNull();
    expect(dialog.refused).toHaveBeenCalledTimes(1);
    expect(dialog.isClosed()).toBe(false);
    expect(dialog.get<HTMLButtonElement>('dashboard-name-save')!.disabled).toBe(false); // free to try again
  });

  it('shows the API\'s message on the widgets as the dialog\'s error (Original above the widget limit)', async () => {
    const dialog = await open('duplicate', housing);
    await dialog.save();

    const message = 'A dashboard can have at most 20 widgets. Remove one or move it to another dashboard first.';
    dialog.http.expectOne(`${dashboardsUrl}/d1/copies`).flush({ title: 'x', errors: { widgets: [message] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.dialogError()).toBe(message);
    expect(dialog.get('dashboard-name-error')!.querySelector('[lang="en"]')).not.toBeNull(); // written by the API
    expect(dialog.fieldError()).toBeNull();
    expect(dialog.isClosed()).toBe(false);
  });

  it('closes with gone when the dashboard no longer exists', async () => {
    const dialog = await open('rename', health);
    await dialog.type('Salud');
    await dialog.save();

    dialog.http.expectOne(`${dashboardsUrl}/d2`).flush({ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' });
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
    expect(dialog.refused).not.toHaveBeenCalled(); // the page runs its own flow for a removed dashboard
  });

  it('is cancelled by Escape, sending nothing', async () => {
    const dialog = await open('duplicate', housing);

    pressKey(dialog.input(), 'Escape');
    await dialog.settle();

    expect(dialog.isClosed()).toBe(true);
    expect(dialog.results).toEqual([undefined]);
    expect(dialog.root()).toBeNull();
    dialog.http.verify();
  });

  describe('the name proposed for a copy', () => {
    it('ends with " (copia)" in the Spanish UI (Name proposed for a duplicate)', async () => {
      const dialog = await open('duplicate', housing, [housing, health], 'es');

      expect(dialog.input().value).toBe('Housing (copia)');
    });

    it('is the name followed by " (copy)", and can be changed before saving (Proposed name)', async () => {
      const dialog = await open('duplicate', housing);
      expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('Duplicate Housing');
      expect(dialog.input().value).toBe('Housing (copy)');

      await dialog.type('Housing 2026');
      await dialog.save();

      const request = dialog.http.expectOne(`${dashboardsUrl}/d1/copies`);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ name: 'Housing 2026' });
      request.flush({ id: 'd9', name: 'Housing 2026' });
      await dialog.settle();
      expect(dialog.results).toEqual([{ id: 'd9', name: 'Housing 2026' }]);
    });

    it('cuts a long name to its first 33 characters (Proposed name for a long name)', async () => {
      const long = { id: 'd5', name: 'Cobertura de vacunación por departamento'.padEnd(40, 'x') };
      expect(long.name).toHaveLength(40);
      const dialog = await open('duplicate', long, [long]);

      expect(dialog.input().value).toBe(`${long.name.slice(0, 33)} (copy)`);
      expect(dialog.input().value).toHaveLength(40);
    });

    it('drops the spaces the cut leaves at the end of the name', async () => {
      // The 33rd character is a space.
      const spaced = { id: 'd5', name: `${'a'.repeat(32)} bcdefg` };
      const dialog = await open('duplicate', spaced, [spaced]);

      expect(dialog.input().value).toBe(`${'a'.repeat(32)} (copy)`);
    });

    it('is " (copy 2)" when "Housing (copy)" is taken, in any case, and " (copy 3)" after that', async () => {
      const first = await open('duplicate', housing, [housing, { id: 'd2', name: 'HOUSING (COPY)' }]);
      expect(first.input().value).toBe('Housing (copy 2)');
      TestBed.resetTestingModule();
      document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = ''));

      const second = await open('duplicate', housing, [housing, { id: 'd2', name: 'Housing (copy)' }, { id: 'd3', name: 'Housing (copy 2)' }]);
      expect(second.input().value).toBe('Housing (copy 3)');
    });

    it('cuts a long name further to make room for the number', async () => {
      const long = { id: 'd5', name: 'b'.repeat(40) };
      const dialog = await open('duplicate', long, [long, { id: 'd6', name: `${'b'.repeat(33)} (copy)` }]);

      expect(dialog.input().value).toBe(`${'b'.repeat(31)} (copy 2)`);
    });
  });
});
