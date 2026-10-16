import { Component, Injector, effect, runInInjectionContext, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { undo } from '@codemirror/commands';
import { Extension } from '@codemirror/state';
import { EditorView } from '@codemirror/view';
import { CodeEditor } from './code-editor';

// jsdom has no layout; CodeMirror measures text ranges after edits.
Range.prototype.getClientRects ??= () => [] as unknown as DOMRectList;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();

@Component({
  imports: [CodeEditor],
  template: `<tmz-code-editor [value]="value()" [readonly]="readonly()" [extensions]="extensions()" label="Clean script" (changed)="edits.push($event)" />`,
})
class Host {
  readonly value = signal('def clean(raw, ctx):\n    return raw\n');
  readonly readonly = signal(false);
  readonly extensions = signal<Extension[]>([]);
  readonly edits: string[] = [];
  readonly editor = viewChild.required(CodeEditor);
}

const keyCodes: Record<string, number> = { Tab: 9, Escape: 27, Enter: 13 };

/** A keydown as a browser sends it; CodeMirror reads the legacy keyCode too, which jsdom leaves at 0. */
function press(target: HTMLElement, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  Object.defineProperty(event, 'keyCode', { value: keyCodes[key] ?? 0 });
  target.dispatchEvent(event);
  return event;
}

describe('CodeEditor', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    const content = fixture.nativeElement.querySelector('.cm-content') as HTMLElement;
    return { fixture, content, view: EditorView.findFromDOM(fixture.nativeElement.querySelector('.cm-editor'))! };
  }

  it('shows the value with Python highlighting and an accessible label', async () => {
    const { fixture, content } = await render();

    expect(content.textContent).toContain('def clean(raw, ctx):');
    expect(content.getAttribute('aria-label')).toBe('Clean script');
    expect(fixture.nativeElement.querySelector('.tok-keyword, .ͼb, [class*="ͼ"]')).not.toBeNull();
  });

  it('emits each change and takes new values from outside', async () => {
    const { fixture } = await render();
    const host = fixture.componentInstance;

    host.editor().replace('print("hi")\n');
    expect(host.edits).toEqual(['print("hi")\n']);
    expect(host.editor().text()).toBe('print("hi")\n');

    host.value.set('def prepare_output(cleaned, ctx):\n    return cleaned\n');
    fixture.detectChanges();
    expect(host.editor().text()).toContain('prepare_output');
  });

  it('blocks edits when read-only', async () => {
    const { fixture } = await render();
    fixture.componentInstance.readonly.set(true);
    fixture.detectChanges();

    const content = fixture.nativeElement.querySelector('.cm-content') as HTMLElement;
    expect(content.getAttribute('contenteditable')).toBe('false');
  });

  describe('indentation', () => {
    it('indents every line of a selection by four spaces with Tab', async () => {
      const { fixture, content, view } = await render();
      fixture.componentInstance.editor().replace('a = 1\nb = 2\nc = 3\n');
      view.dispatch({ selection: { anchor: 0, head: 17 } });

      const event = press(content, 'Tab');

      expect(fixture.componentInstance.editor().text()).toBe('    a = 1\n    b = 2\n    c = 3\n');
      expect(event.defaultPrevented).toBe(true);
    });

    it('removes up to four spaces from the cursor line with Shift+Tab', async () => {
      const { fixture, content, view } = await render();
      fixture.componentInstance.editor().replace('def f():\n        x = 1\n  y = 2\n');
      view.dispatch({ selection: { anchor: 12 } });
      press(content, 'Tab', { shiftKey: true });
      expect(fixture.componentInstance.editor().text()).toBe('def f():\n    x = 1\n  y = 2\n');

      view.dispatch({ selection: { anchor: view.state.doc.line(3).from + 3 } });
      press(content, 'Tab', { shiftKey: true });
      expect(fixture.componentInstance.editor().text()).toBe('def f():\n    x = 1\ny = 2\n');
    });

    it('outdents every selected line with Shift+Tab', async () => {
      const { fixture, content, view } = await render();
      fixture.componentInstance.editor().replace('    a\n    b\n');
      view.dispatch({ selection: { anchor: 0, head: 11 } });

      press(content, 'Tab', { shiftKey: true });

      expect(fixture.componentInstance.editor().text()).toBe('a\nb\n');
    });

    it('inserts spaces up to the next multiple of four with nothing selected, and never a tab', async () => {
      const { fixture, content, view } = await render();
      const editor = fixture.componentInstance.editor();
      editor.replace('ab');
      view.dispatch({ selection: { anchor: 2 } });
      press(content, 'Tab');
      expect(editor.text()).toBe('ab  ');

      press(content, 'Tab');
      expect(editor.text()).toBe('ab      ');

      editor.replace('abcde');
      view.dispatch({ selection: { anchor: 5 } });
      press(content, 'Tab');
      expect(editor.text()).toBe('abcde   ');
      expect(editor.text()).not.toContain('\t');
      expect(view.state.selection.main.head).toBe(8);
    });

    it('moves the focus out with Escape followed by Tab, without inserting anything', async () => {
      const { fixture, content } = await render();
      const before = fixture.componentInstance.editor().text();

      press(content, 'Escape');
      const event = press(content, 'Tab');

      expect(event.defaultPrevented).toBe(false);
      expect(fixture.componentInstance.editor().text()).toBe(before);
    });

    it('does not use Tab when the editor is read-only', async () => {
      const { fixture, content } = await render();
      fixture.componentInstance.readonly.set(true);
      fixture.detectChanges();

      const event = press(content, 'Tab');

      expect(event.defaultPrevented).toBe(false);
      expect(fixture.componentInstance.editor().text()).toBe('def clean(raw, ctx):\n    return raw\n');
    });
  });

  describe('replacements and undo', () => {
    const original = 'def clean(raw, ctx):\n    return raw\n';
    const type = (view: EditorView, text: string) =>
      view.dispatch({ changes: { from: view.state.doc.length, insert: text }, selection: { anchor: view.state.doc.length + text.length }, userEvent: 'input.type' });

    it('undoes a replacement of the whole text as one step, and only that', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      type(view, '# typed before\n');

      editor.replace('def clean(raw, ctx):\n    return raw.copy()\n');
      type(view, '# typed after\n');
      expect(editor.text()).toBe('def clean(raw, ctx):\n    return raw.copy()\n# typed after\n');

      undo(view);
      expect(editor.text()).toBe('def clean(raw, ctx):\n    return raw.copy()\n');
      undo(view);
      expect(editor.text()).toBe(`${original}# typed before\n`);
    });

    it('undoes a replacement made right after typing without taking the typing with it', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      type(view, '# typed\n');

      editor.replace('print(1)\n');
      undo(view);

      expect(editor.text()).toBe(`${original}# typed\n`);
    });

    it('replaces a range, keeps the rest, and undoes it as one step', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      editor.replace('a = 1\n# %%\nb = 2\n# %%\nc = 3\n');
      const from = 'a = 1\n'.length;
      const to = from + '# %%\nb = 2'.length;

      expect(editor.replaceRange(from, to, '# %% Rate\nb = 20')).toBe(true);

      expect(editor.text()).toBe('a = 1\n# %% Rate\nb = 20\n# %%\nc = 3\n');
      expect(fixture.componentInstance.edits.at(-1)).toBe('a = 1\n# %% Rate\nb = 20\n# %%\nc = 3\n');
      undo(view);
      expect(editor.text()).toBe('a = 1\n# %%\nb = 2\n# %%\nc = 3\n');
    });

    it('changes nothing and returns false when a range is replaced in a read-only editor', async () => {
      const { fixture } = await render();
      const host = fixture.componentInstance;
      host.readonly.set(true);
      fixture.detectChanges();
      const edits = host.edits.length;

      expect(host.editor().replaceRange(0, 3, 'xyz')).toBe(false);

      expect(host.editor().text()).toBe(original);
      expect(host.edits.length).toBe(edits);
    });

    it('undoes an insertion at the cursor as one step', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      type(view, 'x = 1\n');

      editor.insertAtCursor('y = 2\n');
      type(view, 'z = 3\n');
      undo(view);
      undo(view);

      expect(editor.text()).toBe(`${original}x = 1\n`);
    });
  });

  describe('cursor line', () => {
    it('is the line of the cursor, starting at the first', async () => {
      const { fixture } = await render();

      expect(fixture.componentInstance.editor().cursorLine()).toBe(1);
    });

    it('follows a selection change without any typing', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      const edits = fixture.componentInstance.edits.length;

      view.dispatch({ selection: { anchor: view.state.doc.line(2).from + 2 } });
      expect(editor.cursorLine()).toBe(2);

      view.dispatch({ selection: { anchor: 0 } });
      expect(editor.cursorLine()).toBe(1);
      expect(fixture.componentInstance.edits.length).toBe(edits);
    });

    it('changes with the text when lines are added above the cursor', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      view.dispatch({ selection: { anchor: view.state.doc.line(2).from } });

      view.dispatch({ changes: { from: 0, insert: 'import pandas as pd\n\n' } });

      expect(editor.cursorLine()).toBe(4);
    });

    it('can be read by an effect, which runs again when the cursor moves', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      const seen: number[] = [];
      const effectRef = runInInjectionContext(TestBed.inject(Injector), () => effect(() => seen.push(editor.cursorLine())));
      TestBed.tick();

      view.dispatch({ selection: { anchor: view.state.doc.line(3).from } });
      TestBed.tick();
      effectRef.destroy();

      expect(seen).toEqual([1, 3]);
    });
  });

  describe('extensions and cursor', () => {
    it('applies the extensions of the screen and takes new ones while it lives', async () => {
      const { fixture, content } = await render();
      expect(content.getAttribute('data-extra')).toBeNull();

      fixture.componentInstance.extensions.set([EditorView.contentAttributes.of({ 'data-extra': 'one' })]);
      fixture.detectChanges();
      expect(content.getAttribute('data-extra')).toBe('one');

      fixture.componentInstance.extensions.set([EditorView.contentAttributes.of({ 'data-extra': 'two' })]);
      fixture.detectChanges();
      expect(content.getAttribute('data-extra')).toBe('two');
    });

    it('inserts text at the cursor and replaces a selection, and reports the cursor line', async () => {
      const { fixture, view } = await render();
      const editor = fixture.componentInstance.editor();
      editor.replace('main = x\nrest\n');
      view.dispatch({ selection: { anchor: 7 } });

      expect(editor.insertAtCursor('"id"')).toBe(true);
      expect(editor.text()).toBe('main = "id"x\nrest\n');
      expect(editor.cursorLine()).toBe(1);

      // The selection `d"x` of `main = "id"x` is replaced.
      view.dispatch({ selection: { anchor: 9, head: 12 } });
      editor.insertAtCursor('y');
      expect(editor.text()).toBe('main = "iy\nrest\n');
      view.dispatch({ selection: { anchor: view.state.doc.length } });
      expect(editor.cursorLine()).toBe(3);
    });

    it('does not insert into a read-only editor', async () => {
      const { fixture } = await render();
      fixture.componentInstance.readonly.set(true);
      fixture.detectChanges();

      expect(fixture.componentInstance.editor().insertAtCursor('x')).toBe(false);
      expect(fixture.componentInstance.editor().text()).toBe('def clean(raw, ctx):\n    return raw\n');
    });
  });
});
