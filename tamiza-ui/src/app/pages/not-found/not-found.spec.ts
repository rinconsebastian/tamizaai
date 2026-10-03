import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { translocoTesting } from '../../../testing/transloco-testing';
import { NotFoundPage } from './not-found';

describe('NotFoundPage', () => {
  it('says the page does not exist and links home', async () => {
    await TestBed.configureTestingModule({
      imports: [NotFoundPage, translocoTesting()],
      providers: [provideRouter([])],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotFoundPage);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('h1')?.textContent).toBe('Page not found');
    expect(element.querySelector('a')?.getAttribute('href')).toBe('/');
  });
});
