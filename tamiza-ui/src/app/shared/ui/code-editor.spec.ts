import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CodeEditor } from './code-editor';

@Component({
  imports: [CodeEditor],
  template: `<tmz-code-editor [value]="value()" [readonly]="readonly()" label="Clean script" (changed)="edits.push($event)" />`,
})
class Host {
  readonly value = signal('def clean(raw, ctx):\n    return raw\n');
  readonly readonly = signal(false);
  readonly edits: string[] = [];
  readonly editor = viewChild.required(CodeEditor);
}

describe('CodeEditor', () => {
  async function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  it('shows the value with Python highlighting and an accessible label', async () => {
    const fixture = await render();
    const content = fixture.nativeElement.querySelector('.cm-content') as HTMLElement;

    expect(content.textContent).toContain('def clean(raw, ctx):');
    expect(content.getAttribute('aria-label')).toBe('Clean script');
    expect(fixture.nativeElement.querySelector('.tok-keyword, .ͼb, [class*="ͼ"]')).not.toBeNull();
  });

  it('emits each change and takes new values from outside', async () => {
    const fixture = await render();
    const host = fixture.componentInstance;

    host.editor().replace('print("hi")\n');
    expect(host.edits).toEqual(['print("hi")\n']);
    expect(host.editor().text()).toBe('print("hi")\n');

    host.value.set('def prepare_output(cleaned, ctx):\n    return cleaned\n');
    fixture.detectChanges();
    expect(host.editor().text()).toContain('prepare_output');
  });

  it('blocks edits when read-only', async () => {
    const fixture = await render();
    fixture.componentInstance.readonly.set(true);
    fixture.detectChanges();

    const content = fixture.nativeElement.querySelector('.cm-content') as HTMLElement;
    expect(content.getAttribute('contenteditable')).toBe('false');
  });
});
