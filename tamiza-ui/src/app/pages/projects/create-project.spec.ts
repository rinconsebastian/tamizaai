import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { byTestId, configureApp, details, settle, typeInto } from '../../../testing/project-harness';

describe('CreateProjectPage', () => {
  async function open() {
    const http = configureApp();
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/projects/new');
    await settle(harness);
    const page = harness.routeNativeElement as HTMLElement;
    const field = (id: string) => page.querySelector<HTMLInputElement>(`#${id}`)!;
    const fill = () => {
      typeInto(field('name'), 'Household survey 2026');
      typeInto(field('assetUid'), 'aTestAsset123');
      typeInto(field('apiToken'), 'secret-token');
    };
    const submit = async () => {
      byTestId<HTMLButtonElement>(page, 'submit')!.click();
      await settle(harness);
      return http.expectOne('/api/v1/projects');
    };
    return { http, harness, page, field, fill, submit };
  }

  it('offers kf, eu and a custom server', async () => {
    const { harness, page, fill, submit } = await open();
    const radios = Array.from(page.querySelectorAll<HTMLInputElement>('input[type="radio"]'));
    expect(radios.map((r) => r.value)).toEqual(['kf', 'eu', 'custom']);

    radios[1].click();
    radios[1].dispatchEvent(new Event('change'));
    await settle(harness);
    fill();
    const request = await submit();

    expect(request.request.body).toEqual({
      name: 'Household survey 2026',
      koboServerUrl: 'https://eu.kobotoolbox.org',
      assetUid: 'aTestAsset123',
      apiToken: 'secret-token',
    });

    radios[2].click();
    radios[2].dispatchEvent(new Event('change'));
    await settle(harness);
    expect(page.querySelector('#koboServerUrl')).not.toBeNull();
  });

  it('shows field errors from a 400 next to the fields', async () => {
    const { harness, page, fill, submit } = await open();
    fill();

    (await submit()).flush({ errors: { assetUid: ['Enter the form asset UID.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle(harness);

    const error = page.querySelector('#assetUid-error');
    expect(error?.textContent).toBe('Enter the form asset UID.');
    expect(page.querySelector('#assetUid')?.getAttribute('aria-describedby')).toBe('assetUid-error');
    expect(byTestId(page, 'form-error')).toBeNull();
  });

  it('shows the translated Kobo error from a 422', async () => {
    const { harness, page, fill, submit } = await open();
    fill();

    (await submit()).flush({ detail: 'raw', code: 'kobo.unauthorized' }, { status: 422, statusText: 'Unprocessable Entity' });
    await settle(harness);

    expect(byTestId(page, 'form-error')?.textContent).toContain('Kobo rejected the API token');
  });

  it('goes to the webhook settings after creating the project', async () => {
    const { harness, fill, submit } = await open();
    fill();

    (await submit()).flush({ project: details('admin'), webhook: { url: 'u', username: 'tamiza', secret: 's', secretUnreadable: false } });
    await settle(harness);

    expect(TestBed.inject(Router).url).toBe('/projects/p1/webhook');
  });
});
