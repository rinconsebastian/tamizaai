import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Disclosure } from './disclosure';

@Component({
  imports: [Disclosure],
  template: `
    <tmz-disclosure heading="Set it up in Kobo" status="7 steps" [open]="open()" (toggled)="events.push($event)">
      <p data-testid="content">Open the form in KoboToolbox.</p>
    </tmz-disclosure>
  `,
})
class Host {
  readonly open = signal(false);
  readonly events: boolean[] = [];
}

const later = () => new Promise<void>((resolve) => setTimeout(resolve, 10));

describe('Disclosure', () => {
  async function create() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const details = fixture.nativeElement.querySelector('details') as HTMLDetailsElement;
    return { fixture, details };
  }

  it('shows its heading in an h3 inside the summary, with the status after it, and starts closed', async () => {
    const { details } = await create();
    expect(details.open).toBe(false);
    expect(details.querySelector('summary h3')?.textContent).toBe('Set it up in Kobo');
    expect(details.querySelector('summary')?.textContent).toContain('7 steps');
  });

  it('opens and closes from the summary and reports each change', async () => {
    const { fixture, details } = await create();
    details.querySelector('summary')!.click();
    await later();
    expect(details.open).toBe(true);
    details.querySelector('summary')!.click();
    await later();
    expect(details.open).toBe(false);
    expect(fixture.componentInstance.events).toEqual([true, false]);
  });

  it('starts open when asked to, and keeps what the reader chose when the input does not change', async () => {
    const { fixture, details } = await create();
    fixture.componentInstance.open.set(true);
    await fixture.whenStable();
    expect(details.open).toBe(true);

    details.querySelector('summary')!.click(); // the reader closes it
    await later();
    await fixture.whenStable();
    expect(details.open).toBe(false);
  });
});
