import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { DashboardSourceData, DashboardSourceDialog, DashboardSourceResult } from './dashboard-source-dialog';

@Component({ template: '<button type="button" data-testid="opener">Actions</button>' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const sourceUrl = '/api/v1/projects/p1/dashboards/d1/source';
const survey = 'Encuesta de mercado de la zona, 478 encuestas';

describe('DashboardSourceDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(source: string | null = null, lang: InterfaceLanguage = 'en') {
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const opener = fixture.nativeElement.querySelector('[data-testid="opener"]') as HTMLButtonElement;
    document.body.appendChild(fixture.nativeElement);
    opener.focus();
    const ref = TestBed.inject(Dialog).open<DashboardSourceResult, DashboardSourceData>(DashboardSourceDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', dashboardId: 'd1', name: 'Teatro', source },
      ariaLabelledBy: 'dashboard-source-dialog-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
    });
    const results: (DashboardSourceResult | undefined)[] = [];
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
    const root = () => document.querySelector('[data-testid="dashboard-source-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const input = () => get<HTMLInputElement>('dashboard-source-input')!;
    const type = async (value: string) => {
      input().value = value;
      input().dispatchEvent(new Event('input'));
      await settle();
    };
    const save = async () => {
      get('dashboard-source-save')!.click();
      await settle();
    };
    const fieldError = () => root()?.querySelector('[data-testid="field-error"]')?.textContent?.trim() ?? null;
    const dialogError = () => get('dashboard-source-error')?.textContent?.trim() ?? null;
    return { http, root, get, input, type, save, settle, fieldError, dialogError, results, opener, isClosed: () => closed };
  }

  it('shows one text field holding the source the dashboard has, with the limit as its hint', async () => {
    const dialog = await open(survey);

    expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('Source of Teatro');
    expect(dialog.root()!.getAttribute('data-testid')).toBe('dashboard-source-dialog');
    expect(dialog.root()!.querySelectorAll('input, textarea')).toHaveLength(1);
    expect(dialog.input().value).toBe(survey);
    expect(dialog.root()!.textContent).toContain('Up to 300 characters');
  });

  it('is in Spanish in the Spanish interface', async () => {
    const dialog = await open(null, 'es');

    expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('Fuente de Teatro');
    expect(dialog.get('dashboard-source-save')!.textContent!.trim()).toBe('Guardar fuente');
  });

  it('saves the source typed, trimmed, and closes with the answer (Source saved, UI part)', async () => {
    const dialog = await open();
    expect(dialog.input().value).toBe('');

    await dialog.type(`  ${survey}  `);
    await dialog.save();

    const request = dialog.http.expectOne(sourceUrl);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ source: survey });
    request.flush({ source: survey });
    await dialog.settle();
    expect(dialog.results).toEqual([{ source: survey }]);
    dialog.http.verify();
  });

  it('keeps 300 characters, and one more is refused before anything is sent', async () => {
    const dialog = await open();

    await dialog.type('a'.repeat(301));
    await dialog.save();

    expect(dialog.fieldError()).toBe('The source can have at most 300 characters.');
    expect(dialog.input().getAttribute('aria-invalid')).toBe('true');
    dialog.http.expectNone(sourceUrl);
    expect(dialog.isClosed()).toBe(false);

    await dialog.type('a'.repeat(300));
    await dialog.save();
    const request = dialog.http.expectOne(sourceUrl);
    expect(request.request.body).toEqual({ source: 'a'.repeat(300) });
    request.flush({ source: 'a'.repeat(300) });
    await dialog.settle();
    expect(dialog.results).toHaveLength(1);
  });

  it('sends null for an empty field and closes with no source (Source removed, UI part)', async () => {
    const dialog = await open(survey);

    await dialog.type('   ');
    await dialog.save();

    const request = dialog.http.expectOne(sourceUrl);
    expect(request.request.body).toEqual({ source: null });
    request.flush({ source: null });
    await dialog.settle();
    expect(dialog.results).toEqual([{ source: null }]);
  });

  it('closes with no source when the answer leaves the key out', async () => {
    const dialog = await open(survey);

    await dialog.type('');
    await dialog.save();

    dialog.http.expectOne(sourceUrl).flush({});
    await dialog.settle();
    expect(dialog.results).toEqual([{ source: null }]);
  });

  it('sends nothing when nothing changed, and closes', async () => {
    const dialog = await open(survey);

    await dialog.type(`  ${survey} `);
    await dialog.save();

    dialog.http.expectNone(sourceUrl);
    expect(dialog.isClosed()).toBe(true);
    expect(dialog.results).toEqual([undefined]);
  });

  it('sends nothing when a dashboard without a source is saved empty', async () => {
    const dialog = await open(null);

    await dialog.save();

    dialog.http.expectNone(sourceUrl);
    expect(dialog.results).toEqual([undefined]);
  });

  it('shows the server\'s refusal on the field, keeps what was typed and stays open (Long source, UI part)', async () => {
    const dialog = await open();
    await dialog.type('Encuesta\u0007');
    await dialog.save();

    dialog.http
      .expectOne(sourceUrl)
      .flush({ title: 'x', errors: { source: ['The source cannot contain line breaks or other control characters.'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.fieldError()).toBe('The source cannot contain line breaks or other control characters.');
    expect(dialog.input().getAttribute('aria-invalid')).toBe('true');
    expect(dialog.dialogError()).toBeNull();
    expect(dialog.input().value).toBe('Encuesta\u0007');
    expect(dialog.isClosed()).toBe(false);
    expect(dialog.get<HTMLButtonElement>('dashboard-source-save')!.disabled).toBe(false);

    // Typing clears the verdict.
    await dialog.type('Encuesta');
    expect(dialog.fieldError()).toBeNull();
  });

  it('shows any other refusal as the dialog\'s error', async () => {
    const dialog = await open();
    await dialog.type(survey);
    await dialog.save();

    dialog.http.expectOne(sourceUrl).flush({ title: 'x', errors: { other: ['Something else.'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.dialogError()).toBe('Something else.');
    expect(dialog.fieldError()).toBeNull();
    expect(dialog.isClosed()).toBe(false);
  });

  it('closes with gone when the dashboard no longer exists', async () => {
    const dialog = await open();
    await dialog.type(survey);
    await dialog.save();

    dialog.http.expectOne(sourceUrl).flush({ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' });
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
  });

  describe('keyboard access', () => {
    it('opens with the focus on the field, and Enter saves', async () => {
      const dialog = await open();
      expect(document.activeElement).toBe(dialog.input());

      await dialog.type(survey);
      dialog.root()!.dispatchEvent(new Event('submit', { cancelable: true }));
      await dialog.settle();

      dialog.http.expectOne(sourceUrl).flush({ source: survey });
      await dialog.settle();
      expect(dialog.results).toEqual([{ source: survey }]);
    });

    it('is cancelled by Escape, sending nothing, with the focus back on the button it was opened from', async () => {
      const dialog = await open(survey);

      pressKey(dialog.input(), 'Escape');
      await dialog.settle();

      expect(dialog.isClosed()).toBe(true);
      expect(dialog.results).toEqual([undefined]);
      expect(dialog.root()).toBeNull();
      expect(document.activeElement).toBe(dialog.opener);
      dialog.http.verify();
    });

    it('is cancelled by its button, with the focus back on the button it was opened from', async () => {
      const dialog = await open(survey);

      dialog.get('dashboard-source-cancel')!.click();
      await dialog.settle();

      expect(dialog.results).toEqual([undefined]);
      expect(document.activeElement).toBe(dialog.opener);
    });

    it('gives the focus back to that button once saved', async () => {
      const dialog = await open();
      await dialog.type(survey);
      await dialog.save();
      dialog.http.expectOne(sourceUrl).flush({ source: survey });
      await dialog.settle();

      expect(dialog.root()).toBeNull();
      expect(document.activeElement).toBe(dialog.opener);
    });

    it('names itself by its title, and the field by its label', async () => {
      const dialog = await open();
      const labelled = document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby');
      expect(labelled).toBe('dashboard-source-dialog-title');
      expect(document.getElementById(labelled!)!.textContent!.trim()).toBe('Source of Teatro');
      expect(dialog.root()!.querySelector('label[for="dashboard-source"]')!.textContent!.trim()).toBe('Source');
    });
  });
});
