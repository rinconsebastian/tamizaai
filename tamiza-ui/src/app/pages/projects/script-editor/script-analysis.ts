import { pythonLanguage } from '@codemirror/lang-python';
import type { InputQuestion, InputReferenceTable, InputTable, ScriptKind } from '../../../core/projects/pipeline.service';
import ctxMembers from './ctx-members.json';

/**
 * What the editor knows about a script without running it: its cells and the problems it can find from its syntax tree.
 * Pure functions over text and the tree, so they run without a browser; the CodeMirror extensions (script-assist.ts) and
 * the UI only present the results. Messages are translation keys with parameters.
 */

export type Tree = ReturnType<typeof pythonLanguage.parser.parse>;
type Node = Tree['topNode'];

export function parse(text: string): Tree {
  return pythonLanguage.parser.parse(text);
}

export interface Contract {
  name: string;
  parameters: [string, string];
}

export const contracts: Record<ScriptKind, Contract> = {
  clean: { name: 'clean', parameters: ['raw', 'ctx'] },
  output: { name: 'prepare_output', parameters: ['cleaned', 'ctx'] },
  indicator: { name: 'compute', parameters: ['cleaned', 'ctx'] },
};

export function signatureOf(kind: ScriptKind): string {
  const { name, parameters } = contracts[kind];
  return `${name}(${parameters.join(', ')})`;
}

// ctx -----------------------------------------------------------------------------------------------------------

export interface CtxMember {
  name: string;
  kind: 'property' | 'method';
  signature: string;
}

/** The members of `ctx` and of its objects, by path (`ctx`, `ctx.form`, `ctx.log`, `ctx.reference`). */
export const ctxMembersByPath = ctxMembers as Record<string, CtxMember[]>;

/** The members of the object at `path` after replacing the script's name for `ctx`: `ctx.form` -> `ctx.form`. */
export function membersOf(path: string): CtxMember[] | undefined {
  return ctxMembersByPath[path];
}

/** The group of a path in the translations (`scripts.ctx.members.<group>.<name>`). */
export function memberGroup(path: string): string {
  return path === 'ctx' ? 'root' : path.slice('ctx.'.length);
}

// cells ---------------------------------------------------------------------------------------------------------

const marker = /^[ \t]*# %%(.*)$/;

export interface Cell {
  /** The text after `# %%`, trimmed. */
  name: string;
  /** The 1-based line of the marker that begins the cell. */
  line: number;
  /** The offset of the marker line's start and end. */
  from: number;
  to: number;
}

/** The cells of a script: one per `# %%` line, in order. Lines before the first marker belong to no cell. */
export function cells(text: string): Cell[] {
  const found: Cell[] = [];
  let offset = 0;
  text.split('\n').forEach((line, index) => {
    const match = marker.exec(line.replace(/\r$/, ''));
    if (match) {
      found.push({ name: match[1].trim(), line: index + 1, from: offset, to: offset + line.length });
    }
    offset += line.length + 1;
  });
  return found;
}

/** The cell a 1-based line falls in, with the line of the next marker (the line a test through it stops at), if any. */
export function cellAt(all: Cell[], line: number): { cell: Cell; index: number; stopLine: number | null } | null {
  let index = -1;
  all.forEach((cell, i) => {
    if (cell.line <= line) {
      index = i;
    }
  });
  return index < 0 ? null : { cell: all[index], index, stopLine: all[index + 1]?.line ?? null };
}

// inputs --------------------------------------------------------------------------------------------------------

/** The tables and names the checks and completions use; null `inputs` means they have not been loaded. */
export interface ScriptInputsInfo {
  /** The tables the script receives; empty until a run has published them. */
  tables: InputTable[];
  referenceTables: InputReferenceTable[];
  questions: InputQuestion[];
}

// problems ------------------------------------------------------------------------------------------------------

export interface Problem {
  from: number;
  to: number;
  severity: 'error' | 'warning';
  /** A translation key under `scripts.checks`, with its parameters. */
  key: string;
  params: Record<string, string>;
}

const maxSyntaxProblems = 3;

function text(source: string, node: Node): string {
  return source.slice(node.from, node.to);
}

function stringValue(source: string, node: Node): string | null {
  const raw = text(source, node);
  const quote = raw[0];
  if ((quote !== '"' && quote !== "'") || raw.length < 2 || raw[raw.length - 1] !== quote || raw.startsWith(quote.repeat(3))) {
    return null;
  }
  return raw.slice(1, -1);
}

/** The contract function: a `def` at the top level with the contract's name. */
function contractFunction(tree: Tree, source: string, kind: ScriptKind): Node | null {
  for (let child = tree.topNode.firstChild; child; child = child.nextSibling) {
    if (child.name === 'FunctionDefinition' && child.getChild('VariableName') && text(source, child.getChild('VariableName')!) === contracts[kind].name) {
      return child;
    }
  }
  return null;
}

/** The names the script gave the contract function's two parameters; the contract's names when it has no such function. */
export function parameterNames(tree: Tree, source: string, kind: ScriptKind): [string, string] {
  const fn = contractFunction(tree, source, kind);
  const list = fn?.getChild('ParamList');
  const names = list ? parameters(list, source) : [];
  return [names[0] ?? contracts[kind].parameters[0], names[1] ?? contracts[kind].parameters[1]];
}

function parameters(list: Node, source: string): string[] {
  const names: string[] = [];
  let expectName = true;
  for (let child = list.firstChild; child; child = child.nextSibling) {
    if (child.name === ',') {
      expectName = true;
    } else if (expectName && child.name === 'VariableName') {
      names.push(text(source, child));
      expectName = false;
    } else if (expectName && child.name !== '(' && child.name !== ')') {
      names.push('');
      expectName = false;
    }
  }
  return names;
}

/** `a.b.c` as ['a', 'b', 'c'] when the node is a chain of attribute reads on a name; null for anything else. */
export function attributePath(node: Node, source: string): string[] | null {
  if (node.name === 'VariableName') {
    return [text(source, node)];
  }
  if (node.name === 'MemberExpression') {
    const property = node.getChild('PropertyName');
    const object = node.firstChild;
    if (property && object && node.getChild('.') && !node.getChild('[')) {
      const base = attributePath(object, source);
      return base ? [...base, text(source, property)] : null;
    }
  }
  return null;
}

/** The part of `ctx.reference["name"]`-like subscripts: the object read and the string it is indexed with. */
function subscript(node: Node, source: string): { object: Node; key: Node; value: string } | null {
  if (node.name !== 'MemberExpression' || !node.getChild('[')) {
    return null;
  }
  const object = node.firstChild;
  const key = node.getChild('String');
  const value = key ? stringValue(source, key) : null;
  return object && key && value !== null ? { object, key, value } : null;
}

function list(names: string[]): string {
  return names.join(', ');
}

/**
 * The problems that can be found without running the script (spec: Checks while editing). With a syntax error only
 * that is reported: the rest of the tree is unreliable after it. `inputs` is null until the panel's data has loaded.
 */
export function check(source: string, tree: Tree, kind: ScriptKind, inputs: ScriptInputsInfo | null): Problem[] {
  const syntax = syntaxProblems(source, tree);
  if (syntax.length) {
    return syntax;
  }

  const problems: Problem[] = [];
  const fn = contractFunction(tree, source, kind);
  if (!fn) {
    problems.push({
      from: 0,
      to: Math.max(1, source.indexOf('\n') < 0 ? source.length : source.indexOf('\n')),
      severity: 'error',
      key: 'scripts.checks.noContract',
      params: { kind, signature: signatureOf(kind) },
    });
  } else {
    const paramList = fn.getChild('ParamList');
    if (paramList && parameters(paramList, source).length !== 2) {
      problems.push({
        from: paramList.from,
        to: paramList.to,
        severity: 'warning',
        key: 'scripts.checks.parameters',
        params: { signature: signatureOf(kind) },
      });
    }
    const [dataName, ctxName] = parameterNames(tree, source, kind);
    const body = fn.getChild('Body');
    if (body) {
      problems.push(...bodyProblems(source, body, dataName, ctxName, inputs));
    }
  }
  problems.push(...indentationProblems(source));
  return problems.sort((a, b) => a.from - b.from);
}

function syntaxProblems(source: string, tree: Tree): Problem[] {
  const problems: Problem[] = [];
  let lastLine = -1;
  tree.iterate({
    enter(node) {
      if (!node.type.isError || problems.length >= maxSyntaxProblems) {
        return;
      }
      const line = source.slice(0, node.from).split('\n').length;
      if (line === lastLine) {
        return;
      }
      lastLine = line;
      // An error node is often empty (something is missing here): mark the character before it, or the one at it.
      const from = node.to > node.from ? node.from : Math.max(0, Math.min(node.from, source.length - 1));
      problems.push({ from, to: Math.max(from + 1, node.to), severity: 'error', key: 'scripts.checks.syntax', params: {} });
    },
  });
  return problems;
}

function bodyProblems(source: string, body: Node, dataName: string, ctxName: string, inputs: ScriptInputsInfo | null): Problem[] {
  const problems: Problem[] = [];
  const assigned = assignedTables(body, source, dataName);
  const known = inputs?.tables.map((t) => t.name) ?? [];
  const references = inputs?.referenceTables.map((t) => t.name);

  const visit = (node: Node): void => {
    const path = attributePath(node, source);
    if (path && path[0] === ctxName && path.length >= 2 && path.length <= 3 && node.name === 'MemberExpression') {
      const parent = ['ctx', ...path.slice(1, -1)].join('.');
      const members = membersOf(parent);
      const property = node.getChild('PropertyName')!;
      if (members && !members.some((m) => m.name === path[path.length - 1])) {
        problems.push({
          from: property.from,
          to: property.to,
          severity: 'warning',
          key: 'scripts.checks.unknownMember',
          params: { object: [ctxName, ...path.slice(1, -1)].join('.'), member: path[path.length - 1], members: list(members.map((m) => m.name)) },
        });
      }
    }

    const index = subscript(node, source);
    if (index) {
      const target = attributePath(index.object, source);
      if (target && target.length === 2 && target[0] === ctxName && target[1] === 'reference' && references && !references.includes(index.value)) {
        problems.push({
          from: index.key.from,
          to: index.key.to,
          severity: 'warning',
          key: references.length ? 'scripts.checks.unknownReference' : 'scripts.checks.noReference',
          params: { name: index.value, available: list(references) },
        });
      } else if (target && target.length === 1 && target[0] === dataName && known.length && !known.includes(index.value) && !assigned.has(index.value)) {
        problems.push({
          from: index.key.from,
          to: index.key.to,
          severity: 'warning',
          key: 'scripts.checks.unknownTable',
          params: { name: index.value, available: list(known) },
        });
      }
    }
    for (let child = node.firstChild; child; child = child.nextSibling) {
      visit(child);
    }
  };
  visit(body);
  return problems;
}

/** The tables the script adds to its input dictionary itself: `raw["summary"] = ...`. */
function assignedTables(body: Node, source: string, dataName: string): Set<string> {
  const names = new Set<string>();
  const visit = (node: Node): void => {
    if (node.name === 'AssignStatement') {
      const target = node.firstChild;
      const index = target ? subscript(target, source) : null;
      const object = index ? attributePath(index.object, source) : null;
      if (index && object && object.length === 1 && object[0] === dataName) {
        names.add(index.value);
      }
    }
    for (let child = node.firstChild; child; child = child.nextSibling) {
      visit(child);
    }
  };
  visit(body);
  return names;
}

/**
 * The variables of the contract function assigned directly from an input table (`main = cleaned["main"]`, with an
 * optional `.copy()`), by variable name: how column completion knows which table a name stands for.
 */
export function tableAliases(tree: Tree, source: string, kind: ScriptKind): Map<string, string> {
  const aliases = new Map<string, string>();
  const fn = contractFunction(tree, source, kind);
  const body = fn?.getChild('Body');
  if (!body) {
    return aliases;
  }
  const [dataName] = parameterNames(tree, source, kind);
  const tableOf = (node: Node | null): string | null => {
    if (!node) {
      return null;
    }
    const index = subscript(node, source);
    const object = index ? attributePath(index.object, source) : null;
    if (index && object && object.length === 1 && object[0] === dataName) {
      return index.value;
    }
    // table.copy()
    if (node.name === 'CallExpression') {
      const callee = node.firstChild;
      if (callee?.name === 'MemberExpression' && callee.getChild('PropertyName') && text(source, callee.getChild('PropertyName')!) === 'copy') {
        return tableOf(callee.firstChild);
      }
    }
    return null;
  };
  const visit = (node: Node): void => {
    if (node.name === 'AssignStatement') {
      const target = node.firstChild;
      const value = node.getChild('AssignOp')?.nextSibling ?? null;
      const table = tableOf(value);
      if (target?.name === 'VariableName' && table !== null) {
        aliases.set(text(source, target), table);
      }
    }
    for (let child = node.firstChild; child; child = child.nextSibling) {
      visit(child);
    }
  };
  visit(body);
  return aliases;
}

/** A line indented with both tabs and spaces, or with tabs when the rest of the script uses spaces (or the reverse). */
function indentationProblems(source: string): Problem[] {
  const problems: Problem[] = [];
  let firstStyle: 'tab' | 'space' | null = null;
  let offset = 0;
  for (const line of source.split('\n')) {
    const indent = /^[ \t]+/.exec(line)?.[0] ?? '';
    if (indent) {
      const style = indent.includes('\t') ? (indent.includes(' ') ? 'both' : 'tab') : 'space';
      if (style === 'both' || (firstStyle !== null && style !== firstStyle)) {
        problems.push({ from: offset, to: offset + indent.length, severity: 'warning', key: 'scripts.checks.mixedIndentation', params: {} });
      } else if (firstStyle === null) {
        firstStyle = style;
      }
    }
    offset += line.length + 1;
  }
  return problems.slice(0, 3);
}
