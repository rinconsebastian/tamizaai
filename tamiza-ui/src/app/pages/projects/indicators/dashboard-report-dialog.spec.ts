import { Dialog } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, ViewContainerRef, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DashboardReport, DashboardReportFailure } from '../../../core/projects/indicators.service';
import { InterfaceLanguage } from '../../../core/i18n/interface-language';
import { provideVisibleElements, tick } from '../../../../testing/cdk';
import { localeTesting, translocoTesting } from '../../../../testing/transloco-testing';
import { DashboardReportData, DashboardReportDialog, DashboardReportResult, parseRecipients } from './dashboard-report-dialog';

@Component({ template: '' })
class Host {
  readonly container = inject(ViewContainerRef);
}

const reportUrl = '/api/v1/projects/p1/dashboards/d1/report';
const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
/** ICU versions differ in which space they put before "AM"; compare with plain spaces. */
const plain = (value: string) => value.replace(/[  ]/g, ' ');

/** "Housing" has an email to two partners on Monday and Thursday at 07:00, sent once, last saved by Ada, in Bogotá's time zone. */
function saved(overrides: Partial<DashboardReport> = {}): DashboardReport {
  return {
    settings: { recipients: ['ana@partner.org', 'luis@city.gov.co'], days: [1, 4], time: '07:00', active: true, locale: 'en-US' },
    timeZone: 'America/Bogota',
    updatedBy: { name: 'Ada Lovelace' },
    updatedAt: '2026-10-26T15:30:00Z', // 10:30 in Bogotá
    lastSentAt: '2026-10-27T12:00:00Z', // 07:00 in Bogotá
    lastFailure: null,
    ...overrides,
  };
}

const none: DashboardReport = { settings: null, timeZone: 'America/Bogota', updatedBy: null, updatedAt: null, lastSentAt: null, lastFailure: null };
const notFound = [{ title: 'x', code: 'dashboards.notFound' }, { status: 404, statusText: 'Not Found' }] as const;

describe('parseRecipients', () => {
  it('reads one address per line, or several separated by commas or semicolons, trimmed, without empty ones', () => {
    expect(parseRecipients('ana@partner.org\r\n luis@city.gov.co ,maria@ngo.org;;\n\n  pedro@x.org  ;')).toEqual([
      'ana@partner.org',
      'luis@city.gov.co',
      'maria@ngo.org',
      'pedro@x.org',
    ]);
    expect(parseRecipients(' \n , ; ')).toEqual([]);
  });
});

describe('DashboardReportDialog', () => {
  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  /** Opens the dialog of "Housing" (`d1`) and, unless `answer` is null, answers the read of its email. */
  async function open(answer: DashboardReport | null = saved(), { lang = 'en', locale = 'en-US' }: { lang?: InterfaceLanguage; locale?: string } = {}) {
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements(), localeTesting(locale)],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Host);
    const ref = TestBed.inject(Dialog).open<DashboardReportResult, DashboardReportData>(DashboardReportDialog, {
      viewContainerRef: fixture.componentInstance.container,
      data: { projectId: 'p1', dashboard: { id: 'd1', name: 'Housing' } },
    });
    const results: (DashboardReportResult | undefined)[] = [];
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
    const read = http.expectOne((r) => r.method === 'GET' && r.url === reportUrl);
    if (answer) {
      read.flush(answer);
      await settle();
    }
    const root = () => document.querySelector('[data-testid="dashboard-report-dialog"]') as HTMLElement | null;
    const get = <T extends HTMLElement = HTMLElement>(id: string) => root()?.querySelector<T>(`[data-testid="${id}"]`) ?? null;
    const recipients = () => get<HTMLTextAreaElement>('dashboard-report-recipients')!;
    const time = () => get<HTMLInputElement>('dashboard-report-time')!;
    const active = () => get<HTMLInputElement>('dashboard-report-active')!;
    const days = () => Array.from(root()?.querySelectorAll<HTMLButtonElement>('[data-testid="dashboard-report-day"]') ?? []);
    const pressed = () => days().filter((d) => d.getAttribute('aria-pressed') === 'true').map((d) => Number(d.dataset['day']));
    const type = async (input: HTMLInputElement | HTMLTextAreaElement, value: string) => {
      input.value = value;
      input.dispatchEvent(new Event('input'));
      await settle();
    };
    const click = async (element: HTMLElement) => {
      element.click();
      await settle();
    };
    /** The error shown next to a field, by the id of its control. */
    const fieldError = (id: string) => text(document.getElementById(`${id}-error`)) || null;
    const isPut = (r: { method: string; url: string }) => r.method === 'PUT' && r.url === reportUrl;
    const isSend = (r: { method: string; url: string }) => r.method === 'POST' && r.url === `${reportUrl}/send`;
    return { http, read, root, get, recipients, time, active, days, pressed, type, click, fieldError, isPut, isSend, settle, results, isClosed: () => closed };
  }

  it('warns that the data leaves Tamiza and that the filter defaults are fixed values (Warning shown)', async () => {
    const dialog = await open();

    expect(text(dialog.root()!.querySelector('h2'))).toBe('Scheduled email for Housing');
    const warning = text(dialog.get('dashboard-report-warning'));
    expect(warning).toContain('The recipients need not be Tamiza users.');
    expect(warning).toContain("They receive this dashboard's data by email, outside Tamiza");
    expect(text(dialog.get('dashboard-report-filters-note'))).toBe(
      "The email uses the dashboard's filter defaults. They are fixed values: a default date range is the same in every email.",
    );
  });

  it('shows the saved settings, the project\'s time zone and the last send, in that zone (Email settings saved)', async () => {
    const dialog = await open();

    expect(dialog.recipients().value).toBe('ana@partner.org\nluis@city.gov.co');
    expect(dialog.pressed()).toEqual([1, 4]);
    expect(dialog.time().value).toBe('07:00');
    expect(dialog.active().checked).toBe(true);
    expect(dialog.active().getAttribute('role')).toBe('switch');
    expect(text(dialog.root()!.querySelector('#report-time')!.closest('tmz-form-field'))).toContain("In the project's time zone, America/Bogota.");
    expect(plain(text(dialog.get('dashboard-report-last-sent')))).toBe('Oct 27, 2026, 7:00 AM');
    expect(plain(text(dialog.get('dashboard-report-last-saved')))).toBe('Oct 26, 2026, 10:30 AM by Ada Lovelace');
    expect(dialog.get('dashboard-report-failure')).toBeNull();
    expect(document.activeElement).toBe(dialog.recipients());
  });

  it('lists the weekdays Monday first, each named in full for screen readers', async () => {
    const english = await open();
    expect(english.days().map((d) => text(d.querySelector('[aria-hidden="true"]')))).toEqual(['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']);
    expect(english.days().map((d) => text(d.querySelector('.sr-only')))).toEqual(['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']);
  });

  it('names the weekdays in the interface\'s regional format', async () => {
    const spanish = await open(saved(), { lang: 'es', locale: 'es-CO' });
    expect(spanish.days().map((d) => text(d.querySelector('[aria-hidden="true"]')))).toEqual(['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom']);
    expect(spanish.days().map((d) => text(d.querySelector('.sr-only')))[2]).toBe('Miércoles');
    expect(text(spanish.root()!.querySelector('h2'))).toBe('Envío programado de Housing');
  });

  it('saves the recipients, days, time and state with the interface\'s locale, and closes with what the API answered', async () => {
    const dialog = await open(saved(), { locale: 'en-GB' });

    await dialog.type(dialog.recipients(), 'ana@partner.org, luis@city.gov.co;\nmaria@ngo.org\n');
    await dialog.click(dialog.days()[0]); // Monday off
    await dialog.click(dialog.days()[6]); // Sunday on
    await dialog.click(dialog.days()[2]); // Wednesday on
    await dialog.type(dialog.time(), '18:30');
    dialog.active().click();
    await dialog.settle();
    expect(dialog.pressed()).toEqual([3, 4, 7]);
    await dialog.click(dialog.get('dashboard-report-save')!);

    const request = dialog.http.expectOne(dialog.isPut);
    expect(request.request.body).toEqual({
      recipients: ['ana@partner.org', 'luis@city.gov.co', 'maria@ngo.org'],
      days: [3, 4, 7],
      time: '18:30',
      active: false,
      locale: 'en-GB',
    });
    const answer = saved({ settings: { ...request.request.body } });
    request.flush(answer);
    await dialog.settle();
    expect(dialog.results).toEqual([{ saved: answer }]);
  });

  it('starts a new email with no recipients, every day at 07:00, active, and offers neither removing nor sending it', async () => {
    const dialog = await open(none);

    expect(dialog.recipients().value).toBe('');
    expect(dialog.pressed()).toEqual([1, 2, 3, 4, 5, 6, 7]);
    expect(dialog.time().value).toBe('07:00');
    expect(dialog.active().checked).toBe(true);
    expect(dialog.get('dashboard-report-status')).toBeNull();
    expect(dialog.get('dashboard-report-remove')).toBeNull();
    expect(dialog.get('dashboard-report-send')).toBeNull();
    expect(dialog.get('dashboard-report-warning')).not.toBeNull();

    await dialog.type(dialog.recipients(), 'ana@partner.org');
    await dialog.click(dialog.get('dashboard-report-save')!);
    expect(dialog.http.expectOne(dialog.isPut).request.body).toEqual({
      recipients: ['ana@partner.org'],
      days: [1, 2, 3, 4, 5, 6, 7],
      time: '07:00',
      active: true,
      locale: 'en-US',
    });
  });

  it('asks for a recipient, a day and a time before sending anything', async () => {
    const dialog = await open(none);
    for (const day of dialog.days()) {
      await dialog.click(day);
    }
    await dialog.type(dialog.time(), '');

    await dialog.click(dialog.get('dashboard-report-save')!);

    expect(dialog.fieldError('report-recipients')).toBe('Enter at least one email address.');
    expect(dialog.fieldError('report-days')).toBe('Choose at least one day.');
    expect(dialog.get('dashboard-report-days')!.getAttribute('aria-describedby')).toBe('report-days-error');
    expect(dialog.fieldError('report-time')).toBe('Enter a time.');
    dialog.http.expectNone(dialog.isPut);
    expect(dialog.isClosed()).toBe(false);
  });

  it('shows the API\'s error next to the recipients, keeps what was typed and stays open (Error next to the field)', async () => {
    const dialog = await open();
    await dialog.type(dialog.recipients(), 'ana@partner.org\nluis at city');
    await dialog.click(dialog.get('dashboard-report-save')!);

    const message = '"luis at city" is not a valid email address.';
    dialog.http
      .expectOne(dialog.isPut)
      .flush({ title: 'x', errors: { recipients: [message], locale: ['The locale must be Spanish or English.'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.fieldError('report-recipients')).toBe(message);
    expect(document.getElementById('report-recipients-error')!.getAttribute('lang')).toBe('en'); // the API's own words
    expect(dialog.recipients().getAttribute('aria-invalid')).toBe('true');
    expect(dialog.recipients().getAttribute('aria-describedby')).toBe('report-recipients-error');
    expect(text(dialog.get('dashboard-report-error'))).toBe('The locale must be Spanish or English.'); // no field shows it
    expect(dialog.recipients().value).toBe('ana@partner.org\nluis at city');
    expect(dialog.isClosed()).toBe(false);
    expect(dialog.get<HTMLButtonElement>('dashboard-report-save')!.disabled).toBe(false);

    await dialog.type(dialog.recipients(), 'ana@partner.org\nluis@city.gov.co');
    expect(dialog.fieldError('report-recipients')).toBeNull();
  });

  it('puts the API\'s errors on the days and the time next to them', async () => {
    const dialog = await open();
    await dialog.click(dialog.get('dashboard-report-save')!);

    dialog.http
      .expectOne(dialog.isPut)
      .flush({ title: 'x', errors: { Days: ['Choose at least one weekday.'], time: ['The time must be HH:mm.'] } }, { status: 400, statusText: 'Bad Request' });
    await dialog.settle();

    expect(dialog.fieldError('report-days')).toBe('Choose at least one weekday.');
    expect(dialog.fieldError('report-time')).toBe('The time must be HH:mm.');
    expect(dialog.get('dashboard-report-error')).toBeNull();
  });

  it('shows Send now as busy until the API answers, then says the email was queued (Send now while busy)', async () => {
    const dialog = await open(saved({ settings: { ...saved().settings!, active: false } }));
    const send = dialog.get<HTMLButtonElement>('dashboard-report-send')!;
    expect(text(document.getElementById(send.getAttribute('aria-describedby')!))).toContain('whether the email is active or paused');

    await dialog.click(send);

    expect(send.disabled).toBe(true);
    expect(send.getAttribute('aria-busy')).toBe('true');
    expect(text(send)).toBe('Sending…');
    expect(dialog.get('dashboard-report-queued')).toBeNull();
    const request = dialog.http.expectOne(dialog.isSend);
    expect(request.request.body).toBeNull();
    request.flush(null, { status: 202, statusText: 'Accepted' });
    await dialog.settle();

    expect(send.disabled).toBe(false);
    expect(send.getAttribute('aria-busy')).toBe('false');
    expect(text(send)).toBe('Send now');
    const queued = dialog.get('dashboard-report-queued')!;
    expect(text(queued)).toBe('The email was queued and will be sent shortly.');
    expect(queued.getAttribute('role')).toBe('status');
    expect(dialog.isClosed()).toBe(false);
    dialog.http.expectNone(dialog.isPut); // the saved settings are sent, the form is not saved
  });

  it.each([
    ['reports.tooSoon', 409, 'Send now was used for this email less than 10 minutes ago. Try again later.'],
    ['reports.none', 409, 'This dashboard has no scheduled email. Save one first.'],
    ['reports.unavailable', 503, 'Scheduled emails are not available on this installation.'],
  ])('says why Send now was refused with %s, translated', async (code, status, message) => {
    const dialog = await open();
    await dialog.click(dialog.get('dashboard-report-send')!);

    dialog.http.expectOne(dialog.isSend).flush({ title: 'x', code }, { status, statusText: 'Refused' });
    await dialog.settle();

    expect(text(dialog.get('dashboard-report-error'))).toBe(message);
    expect(dialog.get('dashboard-report-error')!.querySelector('[lang]')).toBeNull(); // translated from its code
    expect(dialog.get('dashboard-report-queued')).toBeNull();
    expect(dialog.get<HTMLButtonElement>('dashboard-report-send')!.disabled).toBe(false);
  });

  it('says email is off when saving is refused with reports.unavailable', async () => {
    const dialog = await open();
    await dialog.click(dialog.get('dashboard-report-save')!);

    dialog.http.expectOne(dialog.isPut).flush({ title: 'x', code: 'reports.unavailable' }, { status: 503, statusText: 'Service Unavailable' });
    await dialog.settle();

    expect(text(dialog.get('dashboard-report-error'))).toBe('Scheduled emails are not available on this installation.');
    expect(dialog.isClosed()).toBe(false);
  });

  const failures: [string, string | null, string][] = [
    ['email.unavailable', null, 'The email server could not be reached, or did not answer in time.'],
    ['email.authFailed', null, 'Authentication with the email server failed.'],
    ['email.rejected', 'nobody@example.org', 'The email server refused the address nobody@example.org, so no recipient received the email.'],
    ['email.rejected', null, 'The email server refused the message.'],
    ['reports.nothingToSend', null, 'There was nothing to send: the dashboard has no widgets, or none of them could be read.'],
    ['reports.failed', null, 'The email could not be prepared because of an unexpected error. An administrator can find the details in the API log.'],
    ['reports.somethingNew', null, 'Something went wrong. Try again.'],
  ];

  it.each(failures)('shows the last failure %s (address %s) with its time, translated (Failure shown)', async (code, address, reason) => {
    const failure: DashboardReportFailure = { code, at: '2026-10-28T12:00:00Z', address };
    const dialog = await open(saved({ lastFailure: failure }));

    expect(plain(text(dialog.get('dashboard-report-failure')))).toBe(`The last attempt failed on Oct 28, 2026, 7:00 AM: ${reason}`);
  });

  it('shows the last failure in Spanish, naming the refused address', async () => {
    const dialog = await open(saved({ lastFailure: { code: 'email.rejected', at: '2026-10-28T12:00:00Z', address: 'nadie@example.org' } }), { lang: 'es', locale: 'es-CO' });

    expect(plain(text(dialog.get('dashboard-report-failure')))).toBe(
      'El último intento falló el 28 de oct de 2026, 7:00 a. m.: El servidor de correo rechazó la dirección nadie@example.org, así que ningún destinatario recibió el correo.',
    );
  });

  it('says the email was not sent yet when it never was', async () => {
    const dialog = await open(saved({ lastSentAt: null }));

    expect(text(dialog.get('dashboard-report-last-sent'))).toBe('Not sent yet');
  });

  it('removes the email only once the member confirms (Email removed)', async () => {
    const dialog = await open();
    const confirmDialog = () => document.querySelector<HTMLElement>('tmz-confirm-dialog');

    await dialog.click(dialog.get('dashboard-report-remove')!);
    expect(text(confirmDialog()!.querySelector('h2'))).toBe('Remove scheduled email');
    expect(text(confirmDialog()!.querySelector('p'))).toBe('Stop emailing Housing and remove its settings? This cannot be undone.');
    await dialog.click(Array.from(confirmDialog()!.querySelectorAll('button')).find((b) => text(b) === 'Cancel')!);
    dialog.http.expectNone((r) => r.method === 'DELETE');
    expect(dialog.isClosed()).toBe(false);

    await dialog.click(dialog.get('dashboard-report-remove')!);
    await dialog.click(confirmDialog()!.querySelector<HTMLElement>('[data-testid="confirm"]')!);
    const request = dialog.http.expectOne((r) => r.method === 'DELETE' && r.url === reportUrl);
    request.flush(null, { status: 204, statusText: 'No Content' });
    await dialog.settle();

    expect(dialog.results).toEqual([{ removed: true }]);
  });

  it('closes with gone when the dashboard was removed before its email was read', async () => {
    const dialog = await open(null);
    dialog.read.flush(...notFound);
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
  });

  it.each(['dashboard-report-save', 'dashboard-report-send'])('closes with gone when %s finds the dashboard removed meanwhile', async (button) => {
    const dialog = await open();
    await dialog.click(dialog.get(button)!);

    dialog.http.expectOne((r) => r.method !== 'GET').flush(...notFound);
    await dialog.settle();

    expect(dialog.results).toEqual([{ gone: true }]);
  });

  it('says why the email could not be read, with nothing to save', async () => {
    const dialog = await open(null);
    expect(dialog.get('dashboard-report-loading')).not.toBeNull();
    expect(dialog.get('dashboard-report-save')).toBeNull();

    dialog.read.flush(null, { status: 500, statusText: 'Server Error' });
    await dialog.settle();

    expect(dialog.get('dashboard-report-load-error')).not.toBeNull();
    expect(dialog.get('dashboard-report-recipients')).toBeNull();
    expect(dialog.get('dashboard-report-save')).toBeNull();
    expect(dialog.get('dashboard-report-cancel')).not.toBeNull();
    expect(dialog.isClosed()).toBe(false);
  });
});
