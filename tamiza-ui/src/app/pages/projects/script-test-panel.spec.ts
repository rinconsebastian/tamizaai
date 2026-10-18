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

    it("marks the worker's texts as English and leaves the words around them to the interface", async () => {
      const shown = [{ name: 'note', line: 3, rows: 0, table: null, text: 'This value could not be shown: boom' }];
      const root = await create(scriptTest({ steps: [step({ status: 'failed', error: "KeyError: 'age'", log: 'INFO starting', shown })] }));

      expect(byTestId(root, 'test-step-error')?.getAttribute('lang')).toBe('en');
      expect(byTestId(root, 'test-step-log')?.getAttribute('lang')).toBe('en');
      expect(byTestId(root, 'test-shown-text')?.getAttribute('lang')).toBe('en');
      const error = byTestId(root, 'test-error')!;
      expect(error.getAttribute('lang')).toBeNull();
      expect(error.querySelector('[lang="en"]')?.textContent).toBe('KeyError');
      expect(textOf(error)).toBe('An indicator script failed: KeyError');
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

  describe('the count of rows of a table', () => {
    const columns = [{ name: 'value', type: 'integer' as const }];
    const table = (name: string, rows: number, withPreview = true) => ({
      name,
      rows,
      columns: withPreview ? columns : null,
      previewRows: withPreview ? Math.min(rows, 2) : 0,
      preview: withPreview ? [[1]] : null,
      cutCells: null,
    });
    const succeeded = (overrides: Partial<TestStep>) =>
      scriptTest({ status: 'succeeded', error: null, errorCode: null, steps: [step({ step: 'indicator', ...overrides })] });

    it('words the preview note, the table option and the missing preview for one row in the singular and for several in the plural', async () => {
      const several = await create(succeeded({ tables: [table('main', 2), table('lookup', 7, false)] }));
      expect(textOf(byTestId(several, 'test-preview-note'))).toBe('First 2 of 2 rows.');
      expect([...several.querySelectorAll('option')].map((option) => textOf(option))).toEqual(['main (2 rows)', 'lookup (7 rows)']);

      const one = await create(succeeded({ tables: [table('main', 1), table('lookup', 1, false)] }));
      expect(textOf(byTestId(one, 'test-preview-note'))).toBe('First 1 of 1 row.');
      expect([...one.querySelectorAll('option')].map((option) => textOf(option))).toEqual(['main (1 row)', 'lookup (1 row)']);
      const select = byTestId<HTMLSelectElement>(one, 'test-table-select-indicator')!;
      select.value = 'lookup';
      select.dispatchEvent(new Event('change'));
      await settle();
      expect(textOf(byTestId(one, 'test-no-preview'))).toBe('1 row. Only the first 20 tables of a step are previewed.');
    });

    it('words the title of a test on one submission in the singular and on several in the plural', async () => {
      const several = await create(scriptTest({ sampleSize: 500 }));
      expect(textOf(several.querySelector('h2'))).toMatch(/ on 500 submissions$/);

      const one = await create(scriptTest({ sampleSize: 1 }));
      expect(textOf(one.querySelector('h2'))).toMatch(/ on 1 submission$/);
    });

    it('words the note of a table the script showed for one row in the singular and for several in the plural', async () => {
      const shown = (rows: number) => [{ name: 'households', line: 4, rows, table: table('households', rows), text: null }];

      const several = await create(succeeded({ shown: shown(3) }));
      expect(textOf(byTestId(several, 'test-shown-note'))).toBe('households, line 4: first 2 of 3 rows.');

      const one = await create(succeeded({ shown: shown(1) }));
      expect(textOf(byTestId(one, 'test-shown-note'))).toBe('households, line 4: first 1 of 1 row.');
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
