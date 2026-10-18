import { Completion, CompletionContext, CompletionResult } from '@codemirror/autocomplete';
import { pythonLanguage } from '@codemirror/lang-python';
import { syntaxTree } from '@codemirror/language';
import { Diagnostic, linter, lintGutter } from '@codemirror/lint';
import { EditorState, Extension, Prec, RangeSetBuilder } from '@codemirror/state';
import { Decoration, DecorationSet, EditorView, ViewPlugin, ViewUpdate, WidgetType, hoverTooltip, keymap } from '@codemirror/view';
import type { InputColumn, ScriptKind } from '../../../core/projects/pipeline.service';
import { isSingular } from '../../../core/i18n/plural';
import { numberText } from '../../../shared/ui';
import {
  CtxMember,
  ScriptInputsInfo,
  Tree,
  cellAt,
  cells,
  check,
  memberGroup,
  membersOf,
  parameterNames,
  parse,
  tableAliases,
} from './script-analysis';

/**
 * The CodeMirror extensions of the script editor: completion of `ctx`, tables, columns, reference tables and questions;
 * the checks as lint marks; hover for `ctx` members; and the "Run cell" actions. They are rebuilt when the kind of script
 * or the inputs change, which also runs the checks again.
 */

export interface ResolvedProblem {
  from: number;
  to: number;
  /** The 1-based line of `from`. */
  line: number;
  severity: 'error' | 'warning';
  message: string;
  /** The translation key of the check, so a page can add an action to some of them. */
  key: string;
}

export interface AssistOptions {
  kind: ScriptKind;
  /** Null until the inputs have loaded. */
  inputs: ScriptInputsInfo | null;
  translate: (key: string, params?: Record<string, unknown>) => string;
  /** The locale that writes numbers (`es-CO`, `en-US`). */
  locale: string;
  /** Whether the user can start tests: analysts and admins, on the current text rather than an older version. */
  canRun: boolean;
  /** Starts a test through the cell whose next marker is on `stopLine`; null runs the whole script (the last cell). */
  runCell: (stopLine: number | null) => void;
  /** Receives the problems each time the checks run. */
  problems: (problems: ResolvedProblem[]) => void;
}

export function scriptAssist(options: AssistOptions): Extension[] {
  return [
    pythonLanguage.data.of({ autocomplete: (context: CompletionContext) => complete(context, options) }),
    linter((view) => lint(view, options), { delay: 500 }),
    lintGutter(),
    hoverTooltip((view, pos) => hover(view, pos, options)),
    cellMarkers(options),
    Prec.highest(
      keymap.of([
        {
          key: 'Mod-Enter',
          run: (view) => {
            const hit = options.canRun ? cellAt(cells(view.state.doc.toString()), view.state.doc.lineAt(view.state.selection.main.head).number) : null;
            if (!hit) {
              return false;
            }
            options.runCell(hit.stopLine);
            return true;
          },
        },
      ]),
    ),
    theme,
  ];
}

// completion ----------------------------------------------------------------------------------------------------

// `name`, `ctx.reference` or `main["x"]` before a `[` and the start of a string in it.
const subscriptBefore = /([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?:\[(?:"[^"\n]*"|'[^'\n]*')\])*)\[\s*(["'])([^"'\n]*)$/;
const questionBefore = /\b([A-Za-z_]\w*)\.form\.(?:label|name)\(\s*(["'])([^"'\n]*)$/;
const attributeBefore = /([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.(\w*)$/;
const subscriptKeys = /\[(?:"([^"\n]*)"|'([^'\n]*)')\]/g;

function complete(context: CompletionContext, options: AssistOptions): CompletionResult | null {
  const line = context.state.doc.lineAt(context.pos);
  const before = line.text.slice(0, context.pos - line.from);
  const node = syntaxTree(context.state).resolveInner(context.pos, -1);
  if (node.name === 'Comment') {
    return null;
  }
  const names = scriptNames(context.state, options.kind);

  const question = questionBefore.exec(before);
  if (question && question[1] === names.ctx) {
    return result(context.pos - question[3].length, questionOptions(options));
  }
  const index = subscriptBefore.exec(before);
  if (index) {
    return result(context.pos - index[3].length, subscriptOptions(index[1], names, context.state, options));
  }
  const attribute = attributeBefore.exec(before);
  if (attribute && node.name !== 'String') {
    const chain = attribute[1].split('.');
    if (chain[0] !== names.ctx) {
      return null;
    }
    const path = ['ctx', ...chain.slice(1)].join('.');
    const members = membersOf(path);
    return members ? result(context.pos - attribute[2].length, members.map((member) => memberOption(path, member, options))) : null;
  }
  return null;
}

function result(from: number, completions: Completion[]): CompletionResult | null {
  return completions.length ? { from, options: completions, validFor: /^\w*$/ } : null;
}

/** The names the script gave its two parameters, and the variables that stand for input tables. */
function scriptNames(state: EditorState, kind: ScriptKind) {
  const tree = syntaxTree(state) as Tree;
  const source = state.doc.toString();
  const [data, ctx] = parameterNames(tree, source, kind);
  return { data, ctx, aliases: tableAliases(tree, source, kind) };
}

function subscriptOptions(target: string, names: ReturnType<typeof scriptNames>, state: EditorState, options: AssistOptions): Completion[] {
  const inputs = options.inputs;
  if (!inputs) {
    return [];
  }
  const parts = /^([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)((?:\[(?:"[^"\n]*"|'[^'\n]*')\])*)$/.exec(target);
  if (!parts) {
    return [];
  }
  const base = parts[1];
  const keys = [...parts[2].matchAll(subscriptKeys)].map((match) => match[1] ?? match[2]);
  const columns = (table: string, items: InputColumn[]) => items.map((column) => columnOption(column, table, options));

  if (base === names.data && keys.length === 0) {
    return inputs.tables.map((table) => ({
      label: table.name,
      type: 'class',
      detail: table.rows === null ? undefined : rowsText(table.rows, options),
    }));
  }
  if (base === `${names.ctx}.reference` && keys.length === 0) {
    return inputs.referenceTables.map((table) => ({ label: table.name, type: 'class', detail: options.translate('scripts.completion.referenceTable') }));
  }
  if (base === `${names.ctx}.reference` && keys.length === 1) {
    const table = inputs.referenceTables.find((candidate) => candidate.name === keys[0]);
    return table ? columns(table.name, table.columns) : [];
  }
  const table = base === names.data && keys.length === 1 ? keys[0] : keys.length === 0 ? names.aliases.get(base) : undefined;
  if (table !== undefined) {
    const found = inputs.tables.find((candidate) => candidate.name === table);
    if (found) {
      return columns(found.name, found.columns);
    }
  }
  // Anywhere else in a subscript string: the columns of every input table, once each.
  const seen = new Set<string>();
  return inputs.tables.flatMap((candidate) =>
    candidate.columns.filter((column) => !seen.has(column.name) && seen.add(column.name)).map((column) => columnOption(column, candidate.name, options)),
  );
}

/** "480 rows" or "1 row": what the completion of an input table says about its size. */
function rowsText(rows: number, options: AssistOptions): string {
  const key = isSingular(rows, options.locale) ? 'scripts.completion.tableRowsOne' : 'scripts.completion.tableRows';
  return options.translate(key, { count: numberText(rows, options.locale) });
}

function columnOption(column: InputColumn, table: string, options: AssistOptions): Completion {
  return { label: column.name, type: 'property', detail: `${options.translate(`preview.types.${column.type}`)}, ${table}`, info: column.label ?? undefined };
}

function questionOptions(options: AssistOptions): Completion[] {
  return (options.inputs?.questions ?? []).map((question) => ({
    label: question.name,
    type: 'variable',
    detail: question.label ?? question.type,
  }));
}

function memberOption(path: string, member: CtxMember, options: AssistOptions): Completion {
  return {
    label: member.name,
    type: member.kind === 'method' ? 'method' : 'property',
    detail: member.signature.slice(path.length + 1),
    info: describe(path, member, options),
  };
}

function describe(path: string, member: CtxMember, options: AssistOptions): string {
  const key = `scripts.ctx.members.${memberGroup(path)}.${member.name}.description`;
  const text = options.translate(key);
  return text === key ? '' : text;
}

// lint ----------------------------------------------------------------------------------------------------------

/** The checks of the script in `state`, with translated messages and line numbers. */
export function lintProblems(state: EditorState, options: AssistOptions): ResolvedProblem[] {
  const source = state.doc.toString();
  const found = check(source, parse(source), options.kind, options.inputs);
  return found.map((problem) => {
    const from = Math.min(problem.from, source.length);
    const params: Record<string, unknown> = { ...problem.params };
    if (problem.params['kind']) {
      params['script'] = options.translate(`scripts.checks.scriptNames.${problem.params['kind']}`);
    }
    return {
      from,
      to: Math.min(Math.max(problem.to, from), source.length),
      line: state.doc.lineAt(from).number,
      severity: problem.severity,
      message: options.translate(problem.key, params),
      key: problem.key,
    };
  });
}

function lint(view: EditorView, options: AssistOptions): Diagnostic[] {
  const resolved = lintProblems(view.state, options);
  options.problems(resolved);
  return resolved.map(({ from, to, severity, message }) => ({ from, to, severity, message }));
}

// hover ---------------------------------------------------------------------------------------------------------

export interface HoverTarget {
  from: number;
  to: number;
  signature: string;
  description: string;
}

/** The `ctx` member under `pos`, with its signature (using the script's name for `ctx`) and description. */
export function hoverTarget(state: EditorState, pos: number, options: AssistOptions): HoverTarget | null {
  const word = state.wordAt(pos);
  if (!word) {
    return null;
  }
  const line = state.doc.lineAt(word.from);
  const before = line.text.slice(0, word.to - line.from);
  const chain = /((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*)$/.exec(before)?.[1].split('.');
  const names = scriptNames(state, options.kind);
  if (!chain || chain[0] !== names.ctx || chain.length < 2) {
    return null;
  }
  const path = ['ctx', ...chain.slice(1, -1)].join('.');
  const member = membersOf(path)?.find((candidate) => candidate.name === chain[chain.length - 1]);
  if (!member) {
    return null;
  }
  return {
    from: word.from,
    to: word.to,
    signature: member.signature.replace(/^ctx/, names.ctx),
    description: describe(path, member, options),
  };
}

function hover(view: EditorView, pos: number, options: AssistOptions) {
  const target = hoverTarget(view.state, pos, options);
  if (!target) {
    return null;
  }
  return {
    pos: target.from,
    end: target.to,
    above: true,
    create() {
      const dom = document.createElement('div');
      dom.className = 'cm-ctx-hover';
      const signature = document.createElement('code');
      signature.textContent = target.signature;
      const description = document.createElement('p');
      description.textContent = target.description;
      dom.append(signature, description);
      return { dom };
    },
  };
}

// cells ---------------------------------------------------------------------------------------------------------

class RunCellWidget extends WidgetType {
  constructor(
    private readonly label: string,
    private readonly aria: string,
    private readonly stopLine: number | null,
    private readonly run: (stopLine: number | null) => void,
  ) {
    super();
  }

  override eq(other: RunCellWidget): boolean {
    return other.label === this.label && other.aria === this.aria && other.stopLine === this.stopLine;
  }

  override toDOM(): HTMLElement {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'cm-run-cell';
    button.textContent = `▶ ${this.label}`;
    button.setAttribute('aria-label', this.aria);
    button.setAttribute('data-testid', 'run-cell');
    // The editor keeps its selection while the button is used.
    button.addEventListener('mousedown', (event) => event.preventDefault());
    button.addEventListener('click', () => this.run(this.stopLine));
    return button;
  }

  override ignoreEvent(): boolean {
    return true;
  }
}

const markerLine = Decoration.line({ class: 'cm-cell-marker' });

/** Styles each `# %%` line and, when the user can test, ends it with a "Run cell" button. */
function cellMarkers(options: AssistOptions): Extension {
  const build = (state: EditorState): DecorationSet => {
    const builder = new RangeSetBuilder<Decoration>();
    const all = cells(state.doc.toString());
    all.forEach((cell, index) => {
      builder.add(cell.from, cell.from, markerLine);
      if (options.canRun) {
        const name = cell.name || String(index + 1);
        builder.add(
          cell.to,
          cell.to,
          Decoration.widget({
            side: 1,
            widget: new RunCellWidget(
              options.translate('scripts.editor.runCell'),
              options.translate('scripts.editor.runCellNamed', { name }),
              all[index + 1]?.line ?? null,
              options.runCell,
            ),
          }),
        );
      }
    });
    return builder.finish();
  };
  return ViewPlugin.fromClass(
    class {
      decorations: DecorationSet;
      constructor(view: EditorView) {
        this.decorations = build(view.state);
      }
      update(update: ViewUpdate): void {
        if (update.docChanged) {
          this.decorations = build(update.state);
        }
      }
    },
    { decorations: (plugin) => plugin.decorations },
  );
}

const theme = EditorView.baseTheme({
  '.cm-cell-marker': { borderTop: '1px dashed var(--color-default-medium)', color: 'var(--color-brand)', fontWeight: '600' },
  '.cm-run-cell': {
    marginLeft: '1rem',
    padding: '0 0.5rem',
    fontSize: '0.75rem',
    border: '1px solid var(--color-default-medium)',
    borderRadius: '0.25rem',
    backgroundColor: 'var(--color-neutral-secondary-medium)',
    color: 'var(--color-body)',
    cursor: 'pointer',
  },
  '.cm-run-cell:hover': { color: 'var(--color-heading)', backgroundColor: 'var(--color-neutral-tertiary-medium)' },
  '.cm-ctx-hover': { padding: '0.25rem 0.5rem', maxWidth: '24rem' },
  '.cm-ctx-hover p': { margin: '0.25rem 0 0' },
});
