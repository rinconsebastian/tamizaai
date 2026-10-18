import { TestBed } from '@angular/core/testing';
import { ToastRegion, ToastService } from './toast';

describe('ToastRegion', () => {
  function render(dismissLabel?: string) {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(ToastRegion);
    if (dismissLabel !== undefined) {
      fixture.componentRef.setInput('dismissLabel', dismissLabel);
    }
    TestBed.inject(ToastService).info('Saved');
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="toast"] button')!;
  }

  it('names the dismiss button with the label it is given', () => {
    expect(render('Cerrar aviso').getAttribute('aria-label')).toBe('Cerrar aviso');
  });

  it('falls back to an English label when it is given none', () => {
    expect(render().getAttribute('aria-label')).toBe('Dismiss');
  });
});
