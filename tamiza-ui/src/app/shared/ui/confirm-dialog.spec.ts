import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { pressKey, provideVisibleElements, tick } from '../../../testing/cdk';
import { ConfirmDialog } from './confirm-dialog';

@Component({
  template: `<button type="button" id="trigger" (click)="open()">Delete</button>`,
})
class Host {
  private readonly dialog = inject(ConfirmDialog);
  result: boolean | null = null;

  async open(): Promise<void> {
    this.result = await this.dialog.confirm({ title: 'Remove member?', message: 'They lose access.', confirmLabel: 'Remove', cancelLabel: 'Cancel', tone: 'danger' });
  }
}

describe('ConfirmDialog', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [Host], providers: [provideVisibleElements()] }));

  async function openFromTrigger() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const trigger = fixture.nativeElement.querySelector('#trigger') as HTMLButtonElement;
    trigger.focus();
    trigger.click();
    await fixture.whenStable();
    await tick();
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement;
    return { fixture, trigger, dialog };
  }

  afterEach(() => document.querySelectorAll('.cdk-overlay-container').forEach((c) => (c.innerHTML = '')));

  it('opens a labelled dialog and moves focus inside it, trapped by focus anchors', async () => {
    const { dialog } = await openFromTrigger();

    expect(dialog.getAttribute('aria-labelledby')).toBe('tmz-confirm-title');
    expect(dialog.textContent).toContain('Remove member?');
    expect(dialog.contains(document.activeElement)).toBe(true);
    expect(dialog.parentElement!.querySelectorAll('.cdk-focus-trap-anchor').length).toBe(2);
  });

  it('closes on Escape, resolves false and returns focus to the trigger', async () => {
    const { fixture, trigger, dialog } = await openFromTrigger();

    pressKey(dialog, 'Escape');
    await fixture.whenStable();
    await tick();

    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(fixture.componentInstance.result).toBe(false);
    expect(document.activeElement).toBe(trigger);
  });

  it('resolves true when confirmed', async () => {
    const { fixture, dialog } = await openFromTrigger();

    (dialog.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    await tick();

    expect(fixture.componentInstance.result).toBe(true);
  });
});
