import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideVisibleElements, tick } from '../../../testing/cdk';
import { translocoTesting } from '../../../testing/transloco-testing';
import { IndicatorMetadata, SCRIPT_TEST_POLL_MS, ScriptTest, TestStep } from '../../core/projects/pipeline.service';
import { FixTarget } from './script-editor/assistant-state';
import { ScriptTestPanel } from './script-test-panel';

const textOf = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const byTestId = <T extends HTMLElement = HTMLElement>(root: ParentNode, id: string) => root.querySelector<T>(`[data-testid="${id}"]`);

const code = 'def compute(cleaned, ctx):\n    return None\n';
const percent: IndicatorMetadata = { label: 'Households with piped water', description: '', unit: '', format: 'percent' };

const step = (overrides: Partial<TestStep>): TestStep => ({
  step: 'indicator',
  status: 'succeeded',
  startedAt: null,
  finishedAt: null,
  inputRows: null,
  outputRows: null,
  tables: [],
  log: '',
  error: null,
  reused: false,
  stoppedAtLine: null,
  shown: null,
  dashboard: null,
  ...overrides,
});

const facts = {
  measures: ['value'],
  defaultMeasure: 'value',
  ratio: false,
  dimensions: ['municipality'],
  defaultDimension: 'municipality',
  geometry: [],
  rows: 4,
  defaultMeasureNulls: 2,
  defaultMeasureMax: 87.5,
  totals: { value: 120.5 },
  duplicateRows: 0,
  charts: {},
};

function scriptTest(overrides: Partial<ScriptTest> = {}): ScriptTest {
  return {
    id: 't1',
    kind: 'indicator',
    name: 'coverage',
    status: 'failed',
    sampleSize: 500,
    stopAtLine: null,
    code,
    waitingFor: null,
    cleanVersion: 2,
    queuedAt: '2026-10-03T10:00:00Z',
    startedAt: null,
    finishedAt: null,
    errorCode: 'step.indicator',
    error: 'KeyError',
    steps: [step({ step: 'clean' }), step({ status: 'failed', error: "KeyError: 'age'" })],
    ...overrides,
  };
}

describe('ScriptTestPanel', () => {
  let fixture: ComponentFixture<ScriptTestPanel>;
  let http: HttpTestingController;
  let asked: FixTarget[];
  let changed: ScriptTest[];

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
      await tick();
    }
  }

  async function create(test: ScriptTest, inputs: { canAsk?: boolean; draft?: string } = {}) {
    fixture = TestBed.createComponent(ScriptTestPanel);
    fixture.componentRef.setInput('started', test);
    fixture.componentRef.setInput('projectId', 'p1');
    fixture.componentRef.setInput('draft', inputs.draft ?? test.code);
    fixture.componentRef.setInput('metadata', percent);
    fixture.componentRef.setInput('canAsk', inputs.canAsk ?? true);
    fixture.componentInstance.ask.subscribe((target) => asked.push(target));
    fixture.componentInstance.changed.subscribe((value) => changed.push(value));
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    asked = [];
    changed = [];
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideVisibleElements(), { provide: SCRIPT_TEST_POLL_MS, useValue: 10 }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  describe('Ask the assistant', () => {
    it('is offered on the failed step of the draft, and reports the test', async () => {
      const root = await create(scriptTest());

      byTestId<HTMLButtonElement>(root, 'test-step-ask')!.click();

      expect(asked).toEqual([{ testId: 't1' }]);
    });

    it('is not offered when the page says it cannot be (disabled assistant, viewer, older version, another script)', async () => {
      const root = await create(scriptTest(), { canAsk: false });

      expect(byTestId(root, 'test-step-error')).not.toBeNull();
      expect(byTestId(root, 'test-step-ask')).toBeNull();
    });

    it('is not offered on a step that is not the draft', async () => {
      const root = await create(scriptTest({ steps: [step({ step: 'clean', status: 'failed', error: 'boom' })] }));

      expect(byTestId(root, 'test-step-error')).not.toBeNull();
      expect(byTestId(root, 'test-step-ask')).toBeNull();
    });

    it('is offered on a warning, with the params the API keeps and not the figures', async () => {
      const root = await create(scriptTest({ status: 'succeeded', error: null, errorCode: null, steps: [step({ dashboard: facts })] }));
      const buttons = [...root.querySelectorAll<HTMLButtonElement>('[data-testid="view-warning-ask"]')];

      expect(buttons).toHaveLength(2);
      buttons[0].click();
      buttons[1].click();

      expect(asked).toEqual([
        { testId: 't1', warning: { key: 'emptyValues', params: { measure: 'value', count: 2 } } },
        { testId: 't1', warning: { key: 'percentAboveOne', params: {} } },
      ]);
    });

    it('goes away when the editor differs from the text that ran, and the result says to test again', async () => {
      const root = await create(scriptTest({ steps: [step({ step: 'clean' }), step({ status: 'failed', error: 'boom' })] }));
      expect(byTestId(root, 'test-step-ask')).not.toBeNull();

      fixture.componentRef.setInput('draft', code + '# edited\n');
      await settle();

      expect(byTestId(root, 'test-step-ask')).toBeNull();
      expect(textOf(byTestId(root, 'test-stale'))).toContain('Test again before asking the assistant for a fix.');
    });

    it('does not tell a user who cannot ask to test again before asking', async () => {
      const root = await create(scriptTest(), { canAsk: false, draft: code + '# edited\n' });

      expect(textOf(byTestId(root, 'test-stale'))).not.toContain('asking the assistant');
    });
  });

  describe('the changed output', () => {
    it('reports the test it starts from and each refresh of it', async () => {
      const running = scriptTest({ status: 'running', error: null, errorCode: null, steps: [] });
      await create(running);
      expect(changed.map((t) => t.status)).toEqual(['running']);

      await new Promise((resolve) => setTimeout(resolve, 30));
      http.expectOne('/api/v1/projects/p1/script-tests/t1').flush(scriptTest());
      await settle();

      expect(changed.map((t) => t.status)).toEqual(['running', 'failed']);
      expect(changed[1].code).toBe(code);
    });
  });
});
