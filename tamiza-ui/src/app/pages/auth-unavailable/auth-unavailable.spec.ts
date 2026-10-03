import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { AuthService, AuthUnavailableReason } from '../../core/auth/auth.service';
import { translocoTesting } from '../../../testing/transloco-testing';
import { AuthUnavailablePage } from './auth-unavailable';

describe('AuthUnavailablePage', () => {
  async function render(reason: AuthUnavailableReason) {
    await TestBed.configureTestingModule({
      imports: [AuthUnavailablePage, translocoTesting()],
      providers: [{ provide: AuthService, useValue: { unavailableReason: signal(reason) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(AuthUnavailablePage);
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('explains that the authentication service is unreachable', async () => {
    const page = await render('unreachable');
    expect(page.querySelector('h1')?.textContent).toContain('Sign-in is unavailable');
    expect(page.textContent).toContain('could not be reached');
  });

  it('explains that a secure connection is required', async () => {
    const page = await render('insecure-context');
    expect(page.textContent).toContain('needs a secure connection');
  });
});
