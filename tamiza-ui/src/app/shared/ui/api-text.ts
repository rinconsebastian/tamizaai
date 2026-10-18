import { Directive, computed, input } from '@angular/core';

/**
 * Text the API wrote (field messages, the `detail` of an error without a code the UI knows) is English whatever the interface
 * language. The parser records each such text here; whoever renders a text asks whether it came from the API, and marks it
 * `lang="en"` so a screen reader pronounces it right. Recording by value keeps the many signals that hold these texts plain strings.
 */
const written = new Set<string>();

/** The most texts remembered: a session sees a few dozen different messages, so the oldest are dropped past this. */
const limit = 500;

/** Records `text` as written by the API and returns it. */
export function markApiText<T extends string | null | undefined>(text: T): T {
  if (text) {
    written.delete(text);
    written.add(text);
    if (written.size > limit) {
      written.delete(written.values().next().value as string);
    }
  }
  return text;
}

/** Whether the API wrote `text`. */
export function isApiText(text: string | null | undefined): boolean {
  return !!text && written.has(text);
}

/**
 * `<p [tmzApiLang]="message">{{ message }}</p>` marks the element `lang="en"` when the API wrote `message`, and leaves it as the
 * page's language when the UI did.
 */
@Directive({ selector: '[tmzApiLang]', host: { '[attr.lang]': 'lang()' } })
export class ApiLang {
  readonly text = input<string | null | undefined>(null, { alias: 'tmzApiLang' });
  protected readonly lang = computed(() => (isApiText(this.text()) ? 'en' : null));
}
