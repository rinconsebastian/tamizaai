import { ColumnDraft, ColumnMetadata, ColumnMetadataMap } from '../../core/projects/pipeline.service';

// The column names of an indicator as the details form handles them (add-indicator-column-metadata, design D8): the saved map of the
// version, the rows being typed, and what a save sends. Later changes of the program add their properties to each of the three; the
// declared category order (add-declared-category-order, design D9) added the order of a column's values, the values kept at the end and
// the choice to hide numbered prefixes.
// Built with `Object.fromEntries`, which defines own properties, so a column called `__proto__` is a column like any other.

/** A row of the form that says nothing: no label, no order, no value kept last and prefixes shown. */
export const emptyDraft = (): ColumnDraft => ({ label: '', order: [], last: [], stripPrefix: false });

/** Whether a row says anything once its label is trimmed. */
function hasAny(draft: ColumnDraft): boolean {
  return !!draft.label.trim() || draft.order.length > 0 || draft.last.length > 0 || draft.stripPrefix;
}

/** The rows of the details form for the facts a version was saved with: one per column that has a label or any other fact. */
export function draftsOf(columns: ColumnMetadataMap | null | undefined): Record<string, ColumnDraft> {
  return Object.fromEntries(
    Object.entries(columns ?? {})
      .map(([column, facts]): [string, ColumnDraft] => [column, { label: facts?.label ?? '', order: [...(facts?.order ?? [])], last: [...(facts?.last ?? [])], stripPrefix: facts?.stripPrefix === true }])
      .filter(([, draft]) => hasAny(draft)),
  );
}

/**
 * What a save sends for every row that says something: its label once trimmed, and its order, values kept last and prefix choice when it
 * has them. A property that is not set is left out, and so are empty lists and an unticked box. The order of the keys means nothing.
 */
export function columnsOf(drafts: Record<string, ColumnDraft>): ColumnMetadataMap {
  return Object.fromEntries(
    Object.entries(drafts)
      .filter(([, draft]) => hasAny(draft))
      .map(([column, draft]): [string, ColumnMetadata] => {
        const label = draft.label.trim();
        return [
          column,
          {
            ...(label ? { label } : {}),
            ...(draft.order.length ? { order: [...draft.order] } : {}),
            ...(draft.last.length ? { last: [...draft.last] } : {}),
            ...(draft.stripPrefix ? { stripPrefix: true } : {}),
          },
        ];
      }),
  );
}

/**
 * Whether two sets of rows save the same facts: a row that says nothing equals no row, a label is compared trimmed, and the order of the
 * values of a list counts (moving a value makes the details unsaved).
 */
export function sameColumns(a: Record<string, ColumnDraft>, b: Record<string, ColumnDraft>): boolean {
  const left = Object.entries(columnsOf(a));
  const right = columnsOf(b);
  return (
    left.length === Object.keys(right).length &&
    left.every(([column, facts]) => Object.hasOwn(right, column) && JSON.stringify(right[column]) === JSON.stringify(facts))
  );
}
