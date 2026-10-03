import { Component, ElementRef, OnDestroy, afterNextRender, effect, inject, input, output } from '@angular/core';
import { python } from '@codemirror/lang-python';
import { Compartment, EditorState } from '@codemirror/state';
import { EditorView, basicSetup } from 'codemirror';

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
  private view: EditorView | null = null;

  readonly value = input('');
  readonly readonly = input(false);
  readonly label = input('Python code');
  readonly changed = output<string>();

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
            EditorState.tabSize.of(4),
            this.editable.of(this.editableExtensions(this.readonly())),
            EditorView.contentAttributes.of({ 'aria-label': this.label() }),
            EditorView.updateListener.of((update) => {
              if (update.docChanged) {
                this.changed.emit(update.state.doc.toString());
              }
            }),
          ],
        }),
      });
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
      const readonly = this.readonly();
      this.view?.dispatch({ effects: this.editable.reconfigure(this.editableExtensions(readonly)) });
    });
  }

  /** The current text, including edits not yet passed back through `value`. */
  text(): string {
    return this.view?.state.doc.toString() ?? this.value();
  }

  /** Replaces the text as if typed, so `changed` fires (used by file upload and templates). */
  replace(text: string): void {
    const view = this.view;
    view?.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } });
  }

  ngOnDestroy(): void {
    this.view?.destroy();
  }

  private editableExtensions(readonly: boolean) {
    return [EditorState.readOnly.of(readonly), EditorView.editable.of(!readonly)];
  }
}
