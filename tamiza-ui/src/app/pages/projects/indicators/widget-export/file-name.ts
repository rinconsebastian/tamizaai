/** The longest a part of a file name is: the rule of `ExportFileName.Slug` in tamiza-api. */
const maxPartLength = 40;

/**
 * Lower-case ASCII letters and digits separated by single dashes; accents are dropped to their letters, and anything else, an emoji
 * or a control character that reorders text included, is a dash. A text that leaves no letter or digit gives `fallback`. Twin of
 * `ExportFileName.Slug` in tamiza-api; both read `testing/export-file-names.json`.
 */
export function slug(text: string, fallback: string): string {
  let result = '';
  for (const character of text.normalize('NFD')) {
    if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')) {
      result += character;
    } else if (character >= 'A' && character <= 'Z') {
      result += character.toLowerCase();
    } else if (result.length > 0 && !result.endsWith('-') && !/^\p{Mn}$/u.test(character)) {
      result += '-';
    }
  }
  result = result.replace(/^-+|-+$/g, '');
  if (result.length > maxPartLength) {
    result = result.slice(0, maxPartLength).replace(/-+$/, '');
  }
  return result.length === 0 ? fallback : result;
}

export type WidgetFileExtension = 'png' | 'svg' | 'xlsx';

/**
 * The name a widget's file is saved under: the project, the dashboard, the widget's title, the version shown when there is one, and
 * the project's day (`yyyy-mm-dd`, see `isoDay`). Twin of `ExportFileName.ForWidget` in tamiza-api.
 */
export function widgetFileName(
  parts: { project: string; dashboard: string; title: string; version: number | null; day: string; extension: WidgetFileExtension },
): string {
  const version = parts.version === null ? '' : `_v${parts.version}`;
  return `${slug(parts.project, 'project')}_${slug(parts.dashboard, 'dashboard')}_${slug(parts.title, 'widget')}${version}_${parts.day}.${parts.extension}`;
}
