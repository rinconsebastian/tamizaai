import { TestBed } from '@angular/core/testing';
import { CurrentUserService } from '../../core/current-user.service';
import { translocoTesting } from '../../../testing/transloco-testing';
import { HomePage } from './home';
import { signal } from '@angular/core';

describe('HomePage', () => {
  it('greets the current user by name', async () => {
    await TestBed.configureTestingModule({
      imports: [HomePage, translocoTesting()],
      providers: [
        {
          provide: CurrentUserService,
          useValue: { user: signal({ id: '1', name: 'Grace Hopper', email: null, isSuperAdmin: false }) },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(HomePage);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toBe('Welcome, Grace Hopper');
  });
});
