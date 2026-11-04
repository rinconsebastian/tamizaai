import { ColumnDraft, ColumnMetadataMap } from '../../core/projects/pipeline.service';

// The column names of an indicator as the details form handles them (add-indicator-column-metadata, design D8): the saved map of the
// version, the rows being typed, and what a save sends. Later changes of the program add their properties to each of the three.
// Built with `Object.fromEntries`, which defines own properties, so a column called `__proto__` is a column like any other.

/** The rows of the details form for the facts a version was saved with: one per column that has a label. */
export function draftsOf(columns: ColumnMetadataMap | null | undefined): Record<string, ColumnDraft> {
  return Object.fromEntries(
    Object.entries(columns ?? {})
      .filter(([, facts]) => facts?.label?.trim())
      .map(([column, facts]) => [column, { label: facts.label! }]),
  );
}

/** What a save sends: `{ column: { label } }` for every row with a label once trimmed. The order of the keys means nothing. */
export function columnsOf(drafts: Record<string, ColumnDraft>): ColumnMetadataMap {
  return Object.fromEntries(
    Object.entries(drafts)
      .map(([column, draft]) => [column, draft.label.trim()] as const)
      .filter(([, label]) => label)
      .map(([column, label]) => [column, { label }]),
  );
}

/** Whether two sets of rows save the same facts: a row with an empty field equals no row, and a label is compared trimmed. */
export function sameColumns(a: Record<string, ColumnDraft>, b: Record<string, ColumnDraft>): boolean {
  const left = Object.entries(columnsOf(a));
  const right = columnsOf(b);
  return left.length === Object.keys(right).length && left.every(([column, facts]) => Object.hasOwn(right, column) && right[column].label === facts.label);
}
