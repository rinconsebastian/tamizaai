import { FormChoice, FormField, SamplingDimension } from '../../core/projects/projects.service';

/** Questions that record how a survey was taken, not what was asked; a dimension never maps to them. */
const metadataTypes = new Set(['start', 'end', 'today', 'deviceid', 'username', 'audit', 'note']);

/** The form's questions a dimension can map to. */
export function frameFields(fields: FormField[] | null): FormField[] {
  return (fields ?? []).filter((field) => !metadataTypes.has(field.type));
}

/** Why the dashboard cannot count a question reliably: its answers sit in a repeat group, or hold several choices. */
export type FieldIssue = 'repeat' | 'multiple';

export function fieldIssue(field: FormField | undefined): FieldIssue | null {
  if (!field) {
    return null;
  }
  // The API keeps an XLSForm-style row ("select_multiple services") as the type "select_multiple_services", so it matches by prefix like the dashboard does.
  return field.inRepeat ? 'repeat' : field.type.startsWith('select_multiple') ? 'multiple' : null;
}

/*
 * The rules of the import template (design D7), mirrored from the API's SamplingFrameTemplate so the question picker can count
 * rows, show labels and fill the editor. The API stays the authority for the file; both sides are tested with the same cases.
 */

/** The limits of a sampling frame and its files, as the API's SamplingFrameValidator, SamplingFrameImport and SamplingFrameTemplate set them. */
export const maxFrameRows = 10_000;
export const maxFrameQuestions = 10;
export const maxTarget = 1_000_000;
export const maxFileMegabytes = 5;
/** The longest value typed for a question without choices. */
export const maxValueLength = 500;

/** The column the importer reads targets from; a question cannot be named like it. */
export const targetColumn = 'target';

/** The code pyxform adds to the list of an or_other question when it deploys the form. */
export const otherCode = 'other';

/** Why the guided path does not offer a question: coverage cannot count it, or its name is the target column's. */
export type TemplateIssue = FieldIssue | 'target';

export function templateIssue(field: FormField): TemplateIssue | null {
  return fieldIssue(field) ?? (sameText(field.xpath, targetColumn) ? 'target' : null);
}

/** A choice a question offers: its code and label. */
export interface FrameChoice {
  code: string;
  label: string | null;
}

/** White space removed from codes and answers, as the dashboard removes it: spaces, tabs, line breaks and non-breaking spaces. */
export function trimCode(value: string): string {
  return value.replace(/^[ \t\r\n ]+|[ \t\r\n ]+$/g, '');
}

function sameText(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}

/** The form's choice lists by name: codes trimmed and kept once ignoring case, the first in form order winning. */
export function choiceLists(choices: FormChoice[] | null): Map<string, FrameChoice[]> {
  const lists = new Map<string, FrameChoice[]>();
  const seen = new Map<string, Set<string>>();
  for (const choice of choices ?? []) {
    const code = trimCode(choice.name);
    if (!code) {
      continue;
    }
    if (!lists.has(choice.listName)) {
      lists.set(choice.listName, []);
      seen.set(choice.listName, new Set());
    }
    const key = code.toLowerCase();
    if (!seen.get(choice.listName)!.has(key)) {
      seen.get(choice.listName)!.add(key);
      lists.get(choice.listName)!.push({ code, label: choice.label });
    }
  }
  return lists;
}

/** A `select_one` question (not from a file) whose list has at least one choice. */
export function hasChoices(field: FormField, lists: Map<string, FrameChoice[]>): boolean {
  return (
    field.type.startsWith('select_one') &&
    !field.type.startsWith('select_one_from_file') &&
    !!field.listName &&
    (lists.get(field.listName)?.length ?? 0) > 0
  );
}

/** The choices a question offers, or null without choices; an or_other question also offers `other` unless its list has it. */
export function choiceList(field: FormField, lists: Map<string, FrameChoice[]>): FrameChoice[] | null {
  if (!hasChoices(field, lists)) {
    return null;
  }
  const own = lists.get(field.listName!)!;
  return field.orOther && !own.some((choice) => sameText(choice.code, otherCode)) ? [...own, { code: otherCode, label: 'Other' }] : own;
}

/** Whether a choice is one pyxform adds on deployment, rather than one of the list's own. */
export function isAddedOther(field: FormField, lists: Map<string, FrameChoice[]>, choice: FrameChoice): boolean {
  return !!field.orOther && choice.code === otherCode && !(lists.get(field.listName ?? '') ?? []).includes(choice);
}

/** The codes of `list` that `values` name, matched exactly and then ignoring case, in form order. */
export function chosenCodes(list: FrameChoice[], values: string[]): string[] {
  const picked = new Set<number>();
  for (const raw of values) {
    const value = trimCode(raw);
    let index = list.findIndex((choice) => choice.code === value);
    if (index < 0) {
      index = list.findIndex((choice) => sameText(choice.code, value));
    }
    if (index >= 0) {
      picked.add(index);
    }
  }
  return [...picked].sort((a, b) => a - b).map((i) => list[i].code);
}

/**
 * The template's column names for the chosen questions: each name, or its path when another field of the form has the same
 * name (notes and metadata count, so pass the unfiltered field list) or when the name is `target`, ignoring case.
 */
export function templateHeaders(snapshot: FormField[], xpaths: string[]): string[] {
  return xpaths.map((xpath) => {
    const field = snapshot.find((f) => f.xpath === xpath);
    if (!field) {
      return xpath;
    }
    const shared = snapshot.some((other) => other.xpath !== field.xpath && sameText(other.name, field.name));
    return shared || sameText(field.name, targetColumn) ? field.xpath : field.name;
  });
}

/** The first header another one repeats ignoring case, or null; the API refuses a template with such headers. */
export function repeatedHeader(headers: string[]): string | null {
  const seen = new Set<string>();
  for (const header of headers) {
    const key = header.toLowerCase();
    if (seen.has(key)) {
      return header;
    }
    seen.add(key);
  }
  return null;
}

/** Typed values (one per line, or a list): trimmed, blank ones dropped, kept once ignoring case. */
export function normalizeValues(values: string | string[]): string[] {
  const seen = new Set<string>();
  const result: string[] = [];
  for (const raw of typeof values === 'string' ? values.split(/\r?\n/) : values) {
    const value = trimCode(raw);
    if (value && !seen.has(value.toLowerCase())) {
      seen.add(value.toLowerCase());
      result.push(value);
    }
  }
  return result;
}

/** 0 when no list has values, otherwise the product of the non-empty lengths (capped, so it never overflows). */
export function combinationCount(lengths: number[]): number {
  let count = 0;
  for (const length of lengths) {
    if (length > 0) {
      count = count === 0 ? length : Math.min(count * length, Number.MAX_SAFE_INTEGER);
    }
  }
  return count;
}

/** Every combination of the non-empty lists, the first varying slowest; an empty list's value stays blank. */
export function combinations(lists: string[][]): string[][] {
  if (lists.every((list) => list.length === 0)) {
    return [];
  }
  const rows: string[][] = [];
  const positions = lists.map(() => 0);
  for (;;) {
    rows.push(lists.map((list, i) => (list.length ? list[positions[i]] : '')));
    let column = lists.length - 1;
    for (; column >= 0; column--) {
      if (!lists[column].length) {
        continue;
      }
      if (++positions[column] < lists[column].length) {
        break;
      }
      positions[column] = 0;
    }
    if (column < 0) {
      return rows;
    }
  }
}

/** The question a file's column maps to, and the two candidates when the saved frame and the form disagree. */
export interface FieldGuess {
  field: string;
  conflict: { saved: string; form: string } | null;
}

/**
 * Preselects the question of a file's column (design D12), ignoring case: the question the saved frame maps a dimension of that
 * name to, while the form has it; a question whose path is the column name; the only question with that name; the only question
 * with that label. When the saved frame's question differs from a path or name match, nothing is preselected.
 */
export function guessField(fields: FormField[], header: string, saved: SamplingDimension[] = []): FieldGuess {
  const wanted = header.trim().toLowerCase();
  const is = (value: string | null | undefined) => (value ?? '').toLowerCase() === wanted;
  const only = (matches: FormField[]) => (matches.length === 1 ? matches[0].xpath : '');

  const byPath = fields.filter((field) => is(field.xpath));
  const pathMatch = byPath.find((field) => field.xpath === header.trim())?.xpath ?? only(byPath);
  const nameMatch = only(fields.filter((field) => is(field.name)));

  const savedField = saved.find((dimension) => is(dimension.name))?.field;
  if (savedField && fields.some((field) => field.xpath === savedField)) {
    const other = [pathMatch, nameMatch].find((xpath) => xpath && xpath !== savedField);
    return other ? { field: '', conflict: { saved: savedField, form: other } } : { field: savedField, conflict: null };
  }
  return { field: pathMatch || nameMatch || only(fields.filter((field) => is(field.label))), conflict: null };
}
