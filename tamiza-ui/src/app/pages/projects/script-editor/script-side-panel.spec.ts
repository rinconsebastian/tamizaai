import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { fakeAssistantSession } from '../../../../testing/assistant-session';
import { provideVisibleElements, tick } from '../../../../testing/cdk';
import { translocoTesting } from '../../../../testing/transloco-testing';
import { SystemConfigService } from '../../../core/system-config';
import { ASSISTANT_SESSION } from './assistant-state';
import { ScriptSidePanel } from './script-side-panel';

const textOf = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const byTestId = <T extends HTMLElement = HTMLElement>(root: ParentNode, id: string) => root.querySelector<T>(`[data-testid="${id}"]`);

describe('ScriptSidePanel tabs', () => {
  let fixture: ComponentFixture<ScriptSidePanel>;
  let root: HTMLElement;
  let enabled: WritableSignal<boolean>;
  let http: HttpTestingController;

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await tick();
    }
  }

  async function create(canEdit: boolean, assistant = true) {
    enabled = signal(assistant);
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideVisibleElements(),
        { provide: SystemConfigService, useValue: { assistantEnabled: enabled } },
        { provide: ASSISTANT_SESSION, useValue: fakeAssistantSession() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ScriptSidePanel);
    fixture.componentRef.setInput('kind', 'indicator');
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.componentRef.setInput('draft', 'def compute(cleaned, ctx):\n    return None\n');
    root = fixture.nativeElement as HTMLElement;
    await settle();
  }

  const tabs = () => [...root.querySelectorAll('[role="tab"]')].map(textOf);
  const selected = () => [...root.querySelectorAll('[role="tab"]')].find((tab) => tab.getAttribute('aria-selected') === 'true')?.getAttribute('data-testid');
  const choose = async (id: string) => {
    byTestId<HTMLButtonElement>(root, `side-tab-${id}`)!.click();
    await settle();
  };
  const answerSettings = async () => {
    http.match('/api/v1/projects/p1/assistant').forEach((r) => r.flush({ model: 'm', host: 'h' }));
    await settle();
  };

  it('has Tables, Reference and Versions, and the Assistant after them for an analyst of an enabled deployment', async () => {
    await create(true);

    expect(tabs()).toEqual(['Tables', 'Reference', 'Versions', 'Assistant']);
    expect(selected()).toBe('side-tab-inputs');
  });

  it.each([
    ['a viewer', false, true],
    ['a deployment without the assistant', true, false],
  ])('has no Assistant for %s', async (_name, canEdit, assistant) => {
    await create(canEdit, assistant);

    expect(tabs()).toEqual(['Tables', 'Reference', 'Versions']);
  });

  it('shows the versions in their own case', async () => {
    await create(true);
    await choose('versions');

    expect(selected()).toBe('side-tab-versions');
    expect(textOf(root.querySelector('#side-panel-versions'))).toBe('No versions saved yet.');
    expect(byTestId(root, 'assistant-panel')).toBeNull();
  });

  describe('open', () => {
    it('shows the requested tab, again even after the analyst chose another one', async () => {
      await create(true);

      expect(fixture.componentInstance.open('assistant')).toBe(true);
      await settle();
      await answerSettings();
      expect(selected()).toBe('side-tab-assistant');
      expect(byTestId(root, 'assistant-panel')).not.toBeNull();

      await choose('inputs');
      expect(selected()).toBe('side-tab-inputs');

      expect(fixture.componentInstance.open('assistant')).toBe(true);
      await settle();
      await answerSettings();
      expect(selected()).toBe('side-tab-assistant');
    });

    it('puts the focus on the request box when asked to', async () => {
      await create(true);

      fixture.componentInstance.open('assistant', true);
      await settle();
      await answerSettings();

      expect(document.activeElement).toBe(byTestId(root, 'assistant-request'));
    });

    it('refuses a tab that is not available and leaves the current one', async () => {
      await create(false);

      expect(fixture.componentInstance.open('assistant')).toBe(false);
      await settle();

      expect(selected()).toBe('side-tab-inputs');
    });
  });

  it('falls back to Tables when the selected Assistant tab disappears', async () => {
    await create(true);
    await choose('assistant');
    await answerSettings();
    expect(byTestId(root, 'assistant-panel')).not.toBeNull();

    enabled.set(false);
    await settle();

    expect(tabs()).toEqual(['Tables', 'Reference', 'Versions']);
    expect(selected()).toBe('side-tab-inputs');
    expect(byTestId(root, 'assistant-panel')).toBeNull();
  });

  it('falls back to Tables when the user can no longer edit', async () => {
    await create(true);
    await choose('assistant');
    await answerSettings();

    fixture.componentRef.setInput('canEdit', false);
    await settle();

    expect(tabs()).toEqual(['Tables', 'Reference', 'Versions']);
    expect(selected()).toBe('side-tab-inputs');
  });
});
