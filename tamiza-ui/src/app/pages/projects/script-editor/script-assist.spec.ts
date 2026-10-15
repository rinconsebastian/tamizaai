import { CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import { python } from '@codemirror/lang-python';
import { EditorState } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import en from '../../../../../public/i18n/en.json';
import type { ScriptKind } from '../../../core/projects/pipeline.service';
import { ScriptInputsInfo } from './script-analysis';
import { AssistOptions, ResolvedProblem, hoverTarget, lintProblems, scriptAssist } from './script-assist';

// jsdom has no layout; CodeMirror measures text ranges after edits.
Range.prototype.getClientRects ??= () => [] as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

/** The translations as the app has them, with `{{ name }}` filled in. */
function translate(key: string, params: Record<string, unknown> = {}): string {
  const text = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], en);
  return typeof text === 'string' ? text.replace(/\{\{\s*(\w+)\s*\}\}/g, (_, name) => String(params[name] ?? '')) : key;
}

const inputs: ScriptInputsInfo = {
  tables: [
    {
      name: 'main',
      rows: 480,
      columns: [
        { name: 'municipality', type: 'text', label: 'Municipality' },
        { name: 'has_water', type: 'text', label: 'Does the household have running water?' },
      ],
    },
    { name: 'members', rows: 1200, columns: [{ name: 'age', type: 'integer' }] },
  ],
  referenceTables: [{ name: 'population', columns: [{ name: 'population_2026', type: 'integer' }] }],
  questions: [
    { name: 'municipality', path: 'municipality', type: 'select_one', label: 'Municipality' },
    { name: 'age', path: 'members/age', type: 'integer', label: 'Age' },
  ],
};

function options(overrides: Partial<AssistOptions> = {}): AssistOptions {
  return { kind: 'indicator', inputs, translate, canRun: true, runCell: () => undefined, problems: () => undefined, ...overrides };
}

function stateOf(doc: string, overrides: Partial<AssistOptions> = {}) {
  return EditorState.create({ doc, extensions: [python(), scriptAssist(options(overrides))] });
}

/** What the sources offer at the end of `text`: label, type and detail of each option, from all the language's sources. */
async function offered(text: string, overrides: Partial<AssistOptions> = {}) {
  const state = stateOf(text, overrides);
  const context = new CompletionContext(state, text.length, true);
  const results = await Promise.all(state.languageDataAt<(c: CompletionContext) => CompletionResult | null | Promise<CompletionResult | null>>('autocomplete', text.length).map((source) => source(context)));
  return results.flatMap((result) => result?.options ?? []);
}

const labels = async (text: string, overrides: Partial<AssistOptions> = {}) => (await offered(text, overrides)).map((o) => o.label);

describe('script assist: completion of ctx', () => {
  it('offers the members of ctx.form with their signatures after a dot', async () => {
    const found = await offered('def compute(cleaned, ctx):\n    ctx.form.');

    const members = found.filter((o) => ['questions', 'choices', 'columns', 'label', 'name', 'column_label'].includes(o.label));
    expect(members.map((o) => o.label).sort()).toEqual(['choices', 'column_label', 'columns', 'label', 'name', 'questions']);
    expect(found.find((o) => o.label === 'label')).toMatchObject({ type: 'method', detail: 'label(question, code)' });
    expect(found.find((o) => o.label === 'label')!.info).toContain('label of a choice code');
  });

  it('offers the members of ctx and of ctx.log', async () => {
    expect(await labels('def compute(cleaned, ctx):\n    ctx.')).toEqual(expect.arrayContaining(['project_name', 'form', 'log', 'reference', 'show']));
    expect(await labels('def compute(cleaned, ctx):\n    ctx.log.')).toEqual(expect.arrayContaining(['info', 'warning', 'error']));
  });

  it('follows the name the script gave to ctx', async () => {
    expect(await labels('def compute(tables, context):\n    context.form.')).toContain('column_label');
    expect(await labels('def compute(tables, context):\n    ctx.form.')).not.toContain('column_label');
  });

  it('offers the questions of the form as the first argument of ctx.form.label and ctx.form.name', async () => {
    const found = await offered('def compute(cleaned, ctx):\n    ctx.form.label("mun');

    expect(found.map((o) => o.label)).toEqual(['municipality', 'age']);
    expect(found[0].detail).toBe('Municipality');
    expect(await labels('def compute(cleaned, ctx):\n    ctx.form.name("')).toEqual(['municipality', 'age']);
  });
});

describe('script assist: completion of tables and columns', () => {
  it('offers the input tables inside a subscript of the first parameter', async () => {
    const found = await offered('def compute(cleaned, ctx):\n    cleaned["');

    expect(found.map((o) => o.label)).toEqual(['main', 'members']);
    expect(found[0].detail).toBe('480 rows');
  });

  it('offers the columns of the table a variable was assigned from, each with its type and table', async () => {
    const found = await offered('def compute(cleaned, ctx):\n    members = cleaned["members"]\n    main = cleaned["main"]\n    members["');

    expect(found.map((o) => [o.label, o.detail])).toEqual([['age', 'integer, members']]);
  });

  it('offers the columns of a table indexed directly, with the question label as information', async () => {
    const found = await offered('def compute(cleaned, ctx):\n    cleaned["main"]["');

    expect(found.map((o) => [o.label, o.detail])).toEqual([
      ['municipality', 'text, main'],
      ['has_water', 'text, main'],
    ]);
    expect(found[1].info).toBe('Does the household have running water?');
  });

  it('offers the reference tables and the columns of one', async () => {
    expect(await labels('def compute(cleaned, ctx):\n    ctx.reference["')).toEqual(['population']);
    const found = await offered('def compute(cleaned, ctx):\n    ctx.reference["population"]["');
    expect(found.map((o) => [o.label, o.detail])).toEqual([['population_2026', 'integer, population']]);
  });

  it('offers the columns of every input table elsewhere in a subscript string, once each', async () => {
    const found = await labels('def compute(cleaned, ctx):\n    frame["');

    expect(found).toEqual(['municipality', 'has_water', 'age']);
  });

  it('offers no table names while nothing is published, and nothing before the inputs have loaded', async () => {
    expect(await labels('def compute(cleaned, ctx):\n    cleaned["', { inputs: { ...inputs, tables: [] } })).toEqual([]);
    expect(await labels('def compute(cleaned, ctx):\n    cleaned["', { inputs: null })).toEqual([]);
  });

  it('offers nothing inside a comment', async () => {
    expect(await labels('def compute(cleaned, ctx):\n    # cleaned["')).toEqual([]);
  });
});

describe('script assist: checks', () => {
  const problemsOf = (doc: string, kind: ScriptKind = 'output') => lintProblems(stateOf(doc, { kind }), options({ kind }));

  it('words the check of a wrong function name for the kind of script', () => {
    const [problem] = problemsOf('def prepare(cleaned, ctx):\n    return {}\n');

    expect(problem.message).toBe('Script 2 must define prepare_output(cleaned, ctx).');
    expect(problem).toMatchObject({ severity: 'error', line: 1 });
  });

  it('words the check of an unknown reference table and gives its line', () => {
    const [problem] = problemsOf('def compute(cleaned, ctx):\n    x = 1\n    return ctx.reference["populaton"]\n', 'indicator');

    expect(problem.message).toBe('The project has no reference table populaton. Available: population.');
    expect(problem.line).toBe(3);
  });

  it('words the check of a member of ctx that does not exist', () => {
    const [problem] = problemsOf('def compute(cleaned, ctx):\n    ctx.refrence\n    return cleaned["main"]\n', 'indicator');

    expect(problem.message).toBe("ctx has no member 'refrence'. Its members: project_name, form, log, reference, show.");
  });
});

describe('script assist: hover', () => {
  it('gives the signature and description of a member of ctx.form', () => {
    const doc = 'def compute(cleaned, ctx):\n    ctx.form.column_label("main", "age")\n';
    const pos = doc.indexOf('column_label') + 3;

    const target = hoverTarget(stateOf(doc), pos, options());

    expect(target).toMatchObject({ signature: 'ctx.form.column_label(table, column)' });
    expect(target!.description).toBe('The question label of a column of the flattened tables, or None.');
    expect(doc.slice(target!.from, target!.to)).toBe('column_label');
  });

  it('uses the script\'s name for ctx in the signature', () => {
    const doc = 'def compute(cleaned, context):\n    context.show(cleaned["main"])\n';

    expect(hoverTarget(stateOf(doc), doc.indexOf('show') + 1, options())).toMatchObject({ signature: 'context.show(data, name=None)' });
  });

  it('shows nothing for other words', () => {
    const doc = 'def compute(cleaned, ctx):\n    value = ctx.form.nothing\n';

    expect(hoverTarget(stateOf(doc), doc.indexOf('value') + 1, options())).toBeNull();
    expect(hoverTarget(stateOf(doc), doc.indexOf('nothing') + 1, options())).toBeNull();
  });
});

describe('script assist: cells', () => {
  const script = [
    'def compute(cleaned, ctx):',
    '    # %% Load',
    '    main = cleaned["main"]',
    '    # %% Rate',
    '    rate = 1',
    '    # %% Return',
    '    return main',
    '',
  ].join('\n');

  function view(overrides: Partial<AssistOptions> = {}, readOnly = false) {
    const parent = document.createElement('div');
    document.body.append(parent);
    return new EditorView({
      parent,
      state: EditorState.create({
        doc: script,
        extensions: [python(), scriptAssist(options(overrides)), EditorState.readOnly.of(readOnly)],
      }),
    });
  }

  function ctrlEnter(editor: EditorView): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true, cancelable: true });
    Object.defineProperty(event, 'keyCode', { value: 13 });
    editor.contentDOM.dispatchEvent(event);
    return event;
  }

  afterEach(() => (document.body.innerHTML = ''));

  it('puts a Run cell button at the end of each marker line, naming the cell', () => {
    const editor = view();

    const buttons = [...editor.dom.querySelectorAll<HTMLButtonElement>('.cm-run-cell')];

    expect(buttons.map((b) => b.getAttribute('aria-label'))).toEqual(['Run cell Load', 'Run cell Rate', 'Run cell Return']);
    expect(buttons[0].textContent).toContain('Run cell');
    expect(editor.dom.querySelectorAll('.cm-cell-marker')).toHaveLength(3);
  });

  it('starts a test through a cell with the line of the next marker, and a whole test for the last cell', () => {
    const lines: (number | null)[] = [];
    const editor = view({ runCell: (line) => lines.push(line) });

    editor.dom.querySelectorAll<HTMLButtonElement>('.cm-run-cell').forEach((button) => button.click());

    expect(lines).toEqual([4, 6, null]);
  });

  it('runs the cell the cursor is in with Ctrl+Enter', () => {
    const lines: (number | null)[] = [];
    const editor = view({ runCell: (line) => lines.push(line) });
    editor.dispatch({ selection: { anchor: editor.state.doc.line(5).from + 2 } });

    const event = ctrlEnter(editor);

    expect(lines).toEqual([6]);
    expect(event.defaultPrevented).toBe(true);
    expect(editor.state.doc.toString()).toBe(script);
  });

  it('does nothing before the first marker', () => {
    const lines: (number | null)[] = [];
    const editor = view({ runCell: (line) => lines.push(line) });
    editor.dispatch({ selection: { anchor: 3 } });

    ctrlEnter(editor);

    expect(lines).toEqual([]);
  });

  it('shows no button and ignores Ctrl+Enter for someone who cannot test', () => {
    const lines: (number | null)[] = [];
    const editor = view({ canRun: false, runCell: (line) => lines.push(line) }, true);
    editor.dispatch({ selection: { anchor: editor.state.doc.line(3).from } });

    ctrlEnter(editor);

    expect(editor.dom.querySelectorAll('.cm-run-cell')).toHaveLength(0);
    expect(editor.dom.querySelectorAll('.cm-cell-marker')).toHaveLength(3);
    expect(lines).toEqual([]);
    expect(editor.state.doc.toString()).toBe(script);
  });

  it('reports the problems each time the checks run', async () => {
    const reported: ResolvedProblem[][] = [];
    const editor = view({ problems: (problems) => reported.push(problems) });

    editor.dispatch({ changes: { from: editor.state.doc.length, insert: 'x = (\n' } });
    await new Promise((resolve) => setTimeout(resolve, 700));

    expect(reported.at(-1)!.map((p) => p.message)).toEqual(['Syntax error: Python cannot read the code here.']);
  });
});
