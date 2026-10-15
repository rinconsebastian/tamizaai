import { FormField } from '../../core/projects/projects.service';

/** Questions that record how a survey was taken, not what was asked; a dimension never maps to them. */
const metadataTypes = new Set(['start', 'end', 'today', 'deviceid', 'username', 'audit', 'note']);

/** The form's questions a dimension can map to. */
export function frameFields(fields: FormField[] | null): FormField[] {
  return (fields ?? []).filter((field) => !metadataTypes.has(field.type));
}

/** The question whose name, path or label is the one a file calls its dimension, or '' when none is. */
export function guessField(fields: FormField[], name: string): string {
  const wanted = name.trim().toLowerCase();
  return fields.find((field) => [field.name, field.xpath, field.label ?? ''].some((value) => value.toLowerCase() === wanted))?.xpath ?? '';
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
