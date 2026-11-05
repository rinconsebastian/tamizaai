import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { DashboardBases } from '../../../core/projects/indicators.service';
import { DashboardBasesData, DashboardBasesDialog, DashboardBasesResult } from './dashboard-bases-dialog';

@Component({ template: '<button type="button" data-testid="opener">Actions</button>' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const basesUrl = '/api/v1/projects/p1/dashboards/d1/bases';
const off: DashboardBases = { minN: null, showN: false };

// The dialog of the dashboard's base settings (add-small-sample-warnings, design D8).
describe('DashboardBasesDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  async function open(bases: DashboardBases = off, lang: InterfaceLanguage = 'en') {
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const opener = fixture.nativeElement.querySelector('[data-testid="opener"]') as HTMLButtonElement;
    document.body.appendChild(fixture.nativeElement);
    opener.focus();
    const ref = TestBed.inject(Dialog).open<DashboardBasesResult, DashboardBasesData>(DashboardBasesDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', dashboardId: 'd1', name: 'Teatro', bases },
      ariaLabelledBy: 'dashboard-bases-dialog-title',
      autoFocus: 'first-tabbable',
      restoreFocus: true,
    });
    const results: (DashboardBasesResult | undefined)[] = [];
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
    const root = () => document.querySelector('[data-testid="dashboard-bases-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const input = () => get<HTMLInputElement>('dashboard-bases-min')!;
    const checkbox = () => get<HTMLInputElement>('dashboard-bases-show')!;
    const type = async (value: string) => {
      input().value = value;
      input().dispatchEvent(new Event('input'));
      await settle();
    };
    const tick1 = async (on: boolean) => {
      checkbox().checked = on;
      checkbox().dispatchEvent(new Event('change'));
      await settle();
    };
    const save = async () => {
      get('dashboard-bases-save')!.click();
      await settle();
    };
    const fieldError = () => root()?.querySelector('[data-testid="field-error"]')?.textContent?.trim() ?? null;
    const dialogError = () => get('dashboard-bases-error')?.textContent?.trim() ?? null;
    return { http, root, get, input, checkbox, type, tick: tick1, save, settle, fieldError, dialogError, results, opener, isClosed: () => closed };
  }

  it('shows the n line switch and the threshold the dashboard has, with where n comes from', async () => {
    const dialog = await open({ minN: 30, showN: true });

    expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('Base sizes of Teatro');
    expect(dialog.checkbox().checked).toBe(true);
    expect(dialog.input().value).toBe('30');
    expect(dialog.root()!.querySelector('label[for="dashboard-bases-min"]')!.textContent!.trim()).toBe('Dim marks with a base below');
    expect(dialog.root()!.textContent).toContain('Common choices are 30 or 50.');
    expect(dialog.get('dashboard-bases-where')!.textContent).toContain('base column');
  });

  it('starts empty and unchecked for a dashboard without base settings', async () => {
    const dialog = await open();

    expect(dialog.checkbox().checked).toBe(false);
    expect(dialog.input().value).toBe('');
  });

  it('is in Spanish in the Spanish interface', async () => {
    const dialog = await open(off, 'es');

    expect(dialog.root()!.querySelector('h2')!.textContent!.trim()).toBe('Tamaños de base de Teatro');
    expect(dialog.get('dashboard-bases-save')!.textContent!.trim()).toBe('Guardar tamaños de base');
  });

  it('saves the threshold and the n line, and closes with the answer (Analyst sets the threshold, UI part)', async () => {
    const dialog = await open();

    await dialog.type(' 30 ');
    await dialog.tick(true);
    await dialog.save();

    const request = dialog.http.expectOne(basesUrl);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ minN: 30, showN: true });
    request.flush({ minN: 30, showN: true });
    await dialog.settle();
    expect(dialog.results).toEqual([{ minN: 30, showN: true }]);
    dialog.http.verify();
  });

  it('sends null for an empty field, since empty means off', async () => {
    const dialog = await open({ minN: 30, showN: true });

    await dialog.type('   ');
    await dialog.save();

    const request = dialog.http.expectOne(basesUrl);
    expect(request.request.body).toEqual({ minN: null, showN: true });
    request.flush({ showN: true });
    await dialog.settle();
    // A missing minN reads as off.
    expect(dialog.results).toEqual([{ minN: null, showN: true }]);
  });

  it('reads an answer that leaves both settings out as off', async () => {
    const dialog = await open({ minN: 30, showN: true });

    await dialog.type('');
    await dialog.tick(false);
    await dialog.save();

    dialog.http.expectOne(basesUrl).flush({});
    await dialog.settle();
    expect(dialog.results).toEqual([{ minN: null, showN: false }]);
  });

  it.each(['0', '00', '-5', '10001', '12.5', '3 0', 'abc', '1e3'])('refuses the threshold %j before anything is sent', async (typed) => {
    const dialog = await open();

    await dialog.type(typed);
    await dialog.save();

    expect(dialog.fieldError()).toBe('The small-base threshold must be a whole number from 1 to 10,000; leave it empty to turn it off.');
    expect(dialog.input().getAttribute('aria-invalid')).toBe('true');
    dialog.http.expectNone(basesUrl);
    expect(dialog.isClosed()).toBe(false);
  });

  it('accepts the largest threshold once a refused 0 is corrected', async () => {
    const dialog = await open();
    await dialog.type('0');
    await dialog.save();
    expect(dialog.fieldError()).not.toBeNull();

    await dialog.type('10000');
    await dialog.save();
    const request = dialog.http.expectOne(basesUrl);
    expect(request.request.body).toEqual({ minN: 10000, showN: false });
    request.flush({ minN: 10000, showN: false });
    await dialog.settle();
    expect(dialog.results).toEqual([{ minN: 10000, showN: false }]);
  });

  it('sends nothing when nothing changed, and closes (Base settings saved unchanged)', async () => {
    const dialog = await open({ minN: 30, showN: false });

    await dialog.type(' 30');
    await dialog.save();

    dialog.http.expectNone(basesUrl);
    expect(dialog.isClosed()).toBe(true);
    expect(dialog.results).toEqual([undefined]);
  });

  it('sends nothing when a dashboard without base settings is saved as it is', async () => {
    const dialog = await open();

    await dialog.save();

    dialog.http.expectNone(basesUrl);
    expect(dialog.results).toEqual([undefined]);
  });

  it("shows the server's refusal of the threshold on its field, keeps what was typed and stays open (Threshold out of range, UI part)", async () => {
    const dialog = await open();
    await dialog.type('5');
    await dialog.save();

    dialog.http
      .expectOne(basesUrl)
      .flush({ title: 'x', errors: { minN: ['The small-base threshold must be a whole number from 1 to 10,000; leave it empty to turn it off.'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.fieldError()).toBe('The small-base threshold must be a whole number from 1 to 10,000; leave it empty to turn it off.');
    expect(dialog.dialogError()).toBeNull();
    expect(dialog.input().value).toBe('5');
    expect(dialog.isClosed()).toBe(false);
    expect(dialog.get<HTMLButtonElement>('dashboard-bases-save')!.disabled).toBe(false);

    // Typing clears the verdict.
    await dialog.type('50');
    expect(dialog.fieldError()).toBeNull();
  });

  it("shows any other refusal as the dialog's error", async () => {
    const dialog = await open();
    await dialog.type('30');
    await dialog.save();

    dialog.http.expectOne(basesUrl).flush({ title: 'x', errors: { other: ['Something else.'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.dialogError()).toBe('Something else.');
    expect(dialog.fieldError()).toBeNull();
    expect(dialog.isClosed()).toBe(false);
  });

  it('closes with gone when the dashboard no longer exists', async () => {
    const dialog = await open();
    await dialog.type('30');
    await dialog.save();

    dialog.http.expectOne(basesUrl).flush({ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' });
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
  });

  describe('keyboard access', () => {
    it('opens with the focus on the threshold field, and Enter saves', async () => {
      const dialog = await open();
      expect(document.activeElement).toBe(dialog.input());

      await dialog.type('30');
      dialog.root()!.dispatchEvent(new Event('submit', { cancelable: true }));
      await dialog.settle();

      dialog.http.expectOne(basesUrl).flush({ minN: 30, showN: false });
      await dialog.settle();
      expect(dialog.results).toEqual([{ minN: 30, showN: false }]);
    });

    it('lists the controls in a reading order a keyboard follows: the switch, the threshold, Cancel, Save', async () => {
      const dialog = await open();

      const controls = Array.from(dialog.root()!.querySelectorAll<HTMLElement>('input, button')).map((control) => control.dataset['testid']);
      expect(controls).toEqual(['dashboard-bases-show', 'dashboard-bases-min', 'dashboard-bases-cancel', 'dashboard-bases-save']);
      expect(dialog.checkbox().type).toBe('checkbox');
    });

    it('is cancelled by Escape, sending nothing, with the focus back on the button it was opened from', async () => {
      const dialog = await open({ minN: 30, showN: true });

      pressKey(dialog.input(), 'Escape');
      await dialog.settle();

      expect(dialog.isClosed()).toBe(true);
      expect(dialog.results).toEqual([undefined]);
      expect(dialog.root()).toBeNull();
      expect(document.activeElement).toBe(dialog.opener);
      dialog.http.verify();
    });

    it('is cancelled by its button, with the focus back on the button it was opened from', async () => {
      const dialog = await open({ minN: 30, showN: true });

      dialog.get('dashboard-bases-cancel')!.click();
      await dialog.settle();

      expect(dialog.results).toEqual([undefined]);
      expect(document.activeElement).toBe(dialog.opener);
    });

    it('gives the focus back to that button once saved', async () => {
      const dialog = await open();
      await dialog.type('30');
      await dialog.save();
      dialog.http.expectOne(basesUrl).flush({ minN: 30, showN: false });
      await dialog.settle();

      expect(dialog.root()).toBeNull();
      expect(document.activeElement).toBe(dialog.opener);
    });

    it('names itself by its title, and its controls by their labels', async () => {
      const dialog = await open();
      const labelled = document.querySelector('[role="dialog"]')?.getAttribute('aria-labelledby');
      expect(labelled).toBe('dashboard-bases-dialog-title');
      expect(document.getElementById(labelled!)!.textContent!.trim()).toBe('Base sizes of Teatro');
      expect(dialog.root()!.querySelector('label[for="dashboard-bases-min"]')!.textContent!.trim()).toBe('Dim marks with a base below');
      expect(dialog.checkbox().closest('label')!.textContent!.trim()).toBe("Show each widget's base n under it");
    });
  });
});
