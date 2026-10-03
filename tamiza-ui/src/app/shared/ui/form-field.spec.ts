import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormField } from './form-field';
import { Input } from './input';

@Component({
  imports: [FormField, Input],
  template: `
    <tmz-form-field label="Asset UID" fieldId="assetUid" [error]="error()">
      <input tmzInput id="assetUid" />
    </tmz-form-field>
  `,
})
class Host {
  readonly error = signal<string | null>(null);
}

describe('FormField', () => {
  it('shows the error next to the control and links it for assistive technology', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    expect(input.getAttribute('aria-invalid')).toBeNull();
    expect(fixture.nativeElement.querySelector('label').getAttribute('for')).toBe('assetUid');

    fixture.componentInstance.error.set('Enter the asset UID.');
    await fixture.whenStable();

    const message = fixture.nativeElement.querySelector('[data-testid="field-error"]') as HTMLElement;
    expect(message.textContent).toBe('Enter the asset UID.');
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(input.getAttribute('aria-describedby')).toBe(message.id);
    expect(input.className).toContain('bg-danger-soft');
  });
});
