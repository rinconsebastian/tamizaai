import { Component, ElementRef, OnDestroy, afterNextRender, effect, inject, input, output, signal } from '@angular/core';
import { indentLess, indentMore, isolateHistory } from '@codemirror/commands';
import { python } from '@codemirror/lang-python';
import { indentUnit } from '@codemirror/language';
import { Compartment, EditorSelection, EditorState, Extension, Prec, countColumn } from '@codemirror/state';
import { KeyBinding, keymap } from '@codemirror/view';
import { EditorView, basicSetup } from 'codemirror';

const tabSize = 4;

/**
 * Tab indents the lines of a selection by four spaces, or with nothing selected inserts spaces up to the next multiple of
 * four columns; Shift+Tab outdents. A tab character is never inserted. When the editor is read-only the key is not used, and
 * the browser moves the focus. Escape followed by Tab also moves it (CodeMirror's own handling of that pair).
 */
const indentation: KeyBinding = {
  key: 'Tab',
  run: (view) => {
    if (view.state.readOnly) {
      return false;
    }
    if (view.state.selection.ranges.some((range) => !range.empty)) {
      return indentMore(view);
    }
    view.dispatch(
      view.state.changeByRange((range) => {
        const line = view.state.doc.lineAt(range.head);
        const insert = ' '.repeat(tabSize - (countColumn(line.text, tabSize, range.head - line.from) % tabSize));
        return { changes: { from: range.head, insert }, range: EditorSelection.cursor(range.head + insert.length) };
      }),
      { userEvent: 'input.indent', scrollIntoView: true },
    );
    return true;
  },
  shift: indentLess,
};

// Colors come from the Tailwind/Flowbite theme variables, so the editor follows the rest of the UI.
const theme = EditorView.theme({
  '&': { fontSize: '0.875rem', backgroundColor: 'var(--color-neutral-primary-soft)', color: 'var(--color-heading)' },
  '&.cm-focused': { outline: '2px solid var(--color-brand)', outlineOffset: '-1px' },
  '.cm-scroller': { fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace', minHeight: '20rem', maxHeight: '36rem' },
  '.cm-gutters': { backgroundColor: 'var(--color-neutral-secondary-soft)', color: 'var(--color-body)', borderRight: '1px solid var(--color-default)' },
  '&.cm-editor[contenteditable=false], .cm-content[contenteditable=false]': { cursor: 'default' },
});

/**
 * A Python editor (CodeMirror 6). Import it directly, not from the `shared/ui` index, so it stays in the lazily loaded
 * chunk of the screens that use it.
 */
@Component({
  selector: 'tmz-code-editor',
  template: '',
  host: { class: 'block overflow-hidden rounded-base border border-default-medium', '[attr.data-testid]': '"code-editor"' },
})
export class CodeEditor implements OnDestroy {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly editable = new Compartment();
  private readonly extra = new Compartment();
  private view: EditorView | null = null;

  readonly value = input('');
  readonly readonly = input(false);
  readonly label = input('Python code');
  /** Extensions added by the screen (completion, checks, cell actions); they can change while the editor lives. */
  readonly extensions = input<Extension[]>([]);
  readonly changed = output<string>();

  private readonly line = signal(1);
  /**
   * The 1-based line the cursor is on. A signal, so what depends on it (the assistant's "This cell" scope) follows the cursor
   * as it moves without any typing: it is set on every selection and document change.
   */
  readonly cursorLine = this.line.asReadonly();

  constructor() {
    afterNextRender(() => {
      this.view = new EditorView({
        parent: this.host.nativeElement,
        state: EditorState.create({
          doc: this.value(),
          extensions: [
            basicSetup,
            python(),
            theme,
            EditorState.tabSize.of(tabSize),
            indentUnit.of(' '.repeat(tabSize)),
            Prec.high(keymap.of([indentation])),
            this.extra.of(this.extensions()),
            this.editable.of(this.editableExtensions(this.readonly())),
            EditorView.contentAttributes.of({ 'aria-label': this.label() }),
            EditorView.updateListener.of((update) => {
              if (update.selectionSet || update.docChanged) {
                this.line.set(update.state.doc.lineAt(update.state.selection.main.head).number);
              }
              if (update.docChanged) {
                this.changed.emit(update.state.doc.toString());
              }
            }),
          ],
        }),
      });
      this.line.set(this.view.state.doc.lineAt(this.view.state.selection.main.head).number);
    });

    // A new value from outside (another script or version) replaces the text; the editor's own edits do not loop.
    effect(() => {
      const value = this.value();
      const view = this.view;
      if (view && view.state.doc.toString() !== value) {
        view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } });
      }
    });
    effect(() => {
      const extensions = this.extensions();
      this.view?.dispatch({ effects: this.extra.reconfigure(extensions) });
    });
    effect(() => {
      const readonly = this.readonly();
      this.view?.dispatch({ effects: this.editable.reconfigure(this.editableExtensions(readonly)) });
    });
  }

  /** The current text, including edits not yet passed back through `value`. */
  text(): string {
    return this.view?.state.doc.toString() ?? this.value();
  }

  /**
   * Replaces the text as if typed, so `changed` fires (used by file upload, templates and the assistant). The edit is its own
   * undo step: one Ctrl+Z brings back the text from before it, whatever was typed around it.
   */
  replace(text: string): void {
    const view = this.view;
    view?.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text }, annotations: isolateHistory.of('full'), userEvent: 'input.replace' });
  }

  /** Replaces the text between two offsets as one undo step. False, and nothing changes, when the editor is read-only. */
  replaceRange(from: number, to: number, text: string): boolean {
    const view = this.view;
    if (!view || view.state.readOnly) {
      return false;
    }
    const length = view.state.doc.length;
    view.dispatch({
      changes: { from: Math.max(0, Math.min(from, length)), to: Math.max(0, Math.min(to, length)), insert: text },
      annotations: isolateHistory.of('full'),
      userEvent: 'input.replace',
      scrollIntoView: true,
    });
    return true;
  }

  /** Inserts text at the cursor, replacing the selection, and puts the cursor after it. False when the editor is read-only. */
  insertAtCursor(text: string): boolean {
    const view = this.view;
    if (!view || view.state.readOnly) {
      return false;
    }
    view.dispatch(view.state.replaceSelection(text), { scrollIntoView: true, userEvent: 'input.paste', annotations: isolateHistory.of('full') });
    view.focus();
    return true;
  }

  /** Moves the cursor to `position`, scrolls it into view and focuses the editor. */
  goTo(position: number): void {
    const view = this.view;
    if (view) {
      view.dispatch({ selection: { anchor: Math.min(position, view.state.doc.length) }, scrollIntoView: true });
      view.focus();
    }
  }

  ngOnDestroy(): void {
    this.view?.destroy();
  }

  private editableExtensions(readonly: boolean) {
    return [EditorState.readOnly.of(readonly), EditorView.editable.of(!readonly)];
  }
}
