import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Tone } from './badge';
import { Alert } from './alert';

@Component({
  imports: [Alert],
  template: `<tmz-alert [tone]="tone()" [announce]="announce()">Filter removed</tmz-alert>`,
})
class Host {
  readonly tone = signal<Tone>('warning');
  readonly announce = signal(false);
}

describe('Alert', () => {
  async function role(tone: Tone, announce: boolean) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.tone.set(tone);
    fixture.componentInstance.announce.set(announce);
    await fixture.whenStable();
    return (fixture.nativeElement.querySelector('tmz-alert') as HTMLElement).getAttribute('role');
  }

  it('is announced as an alert when dangerous, whatever else is set', async () => {
    expect(await role('danger', false)).toBe('alert');
    expect(await role('danger', true)).toBe('alert');
  });

  it('is announced as a status only when the page asks for it', async () => {
    expect(await role('warning', true)).toBe('status');
    expect(await role('brand', true)).toBe('status');
  });

  it('has no live-region role when it is just part of the page', async () => {
    expect(await role('warning', false)).toBeNull();
  });
});
