import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { vi } from 'vitest';
import { routes } from '../app/app.routes';
import { AuthService } from '../app/core/auth/auth.service';
import { FormField, ProjectDetails, ProjectRole } from '../app/core/projects/projects.service';
import { provideVisibleElements, tick } from './cdk';
import { translocoTesting } from './transloco-testing';

export const projectId = 'p1';

export function details(role: ProjectRole, overrides: Partial<ProjectDetails> = {}): ProjectDetails {
  return {
    id: projectId,
    name: 'Household survey 2026',
    koboServerUrl: 'https://kf.kobotoolbox.org',
    assetUid: 'aTestAsset123',
    formName: 'Household survey',
    formCheckedAt: '2026-10-02T12:00:00Z',
    enumeratorField: null,
    fieldCheck: [
      { metric: 'dailyEvolution', available: true, missingFields: [], hint: null },
      { metric: 'completionTime', available: false, missingFields: ['start'], hint: null },
      { metric: 'perEnumerator', available: false, missingFields: ['username'], hint: 'selectEnumeratorField' },
      { metric: 'coverage', available: true, missingFields: [], hint: null },
    ],
    myRole: role,
    tokenUnreadable: false,
    createdAt: '2026-10-01T12:00:00Z',
    ...overrides,
  };
}

export const formFields: FormField[] = [
  { name: 'start', xpath: 'start', type: 'start', label: null },
  { name: 'municipality', xpath: 'municipality', type: 'select_one', label: 'Municipality' },
  { name: 'sex', xpath: 'sex', type: 'select_one', label: 'Sex' },
  { name: 'enumerator', xpath: 'enumerator', type: 'text', label: 'Enumerator' },
];

export function configureApp() {
  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [
      provideRouter(routes, withComponentInputBinding()),
      provideHttpClient(),
      provideHttpClientTesting(),
      provideVisibleElements(),
      { provide: AuthService, useValue: { status: signal('authenticated'), login: vi.fn(), unavailableReason: signal(null) } },
    ],
  });
  return TestBed.inject(HttpTestingController);
}

/** Opens a project section the way the router would, answering the project and form-field requests. */
export async function openProjectSection(section: string, role: ProjectRole, overrides: Partial<ProjectDetails> = {}) {
  const http = configureApp();
  const harness = await RouterTestingHarness.create();
  const navigation = harness.navigateByUrl(`/projects/${projectId}/${section}`);
  await settle(harness);
  http.expectOne(`/api/v1/projects/${projectId}`).flush(details(role, overrides));
  await navigation;
  await settle(harness);
  http.match(`/api/v1/projects/${projectId}/form-fields`).forEach((r) => r.flush(formFields));
  await settle(harness);
  return { http, harness, element: harness.routeNativeElement as HTMLElement, root: harness.fixture.nativeElement as HTMLElement };
}

export async function settle(harness: RouterTestingHarness): Promise<void> {
  for (let i = 0; i < 3; i++) {
    harness.fixture.detectChanges();
    await harness.fixture.whenStable();
    await tick();
  }
}

export function byTestId<T extends HTMLElement = HTMLElement>(root: ParentNode, id: string): T | null {
  return root.querySelector<T>(`[data-testid="${id}"]`);
}

export function typeInto(input: HTMLInputElement | HTMLSelectElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event(input instanceof HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
}
