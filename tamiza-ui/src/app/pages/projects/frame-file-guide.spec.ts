import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormChoice, FormField } from '../../core/projects/projects.service';
import { translocoTesting } from '../../../testing/transloco-testing';
import { FrameFileGuide } from './frame-file-guide';
import { maxFileMegabytes, maxFrameQuestions } from './sampling-frame-fields';

@Component({
  imports: [FrameFileGuide],
  template: `<app-frame-file-guide [fields]="fields()" [choices]="choices()" [templateLink]="link()" (template)="asked = asked + 1" />`,
})
class Host {
  readonly fields = signal<FormField[] | null>(null);
  readonly choices = signal<FormChoice[] | null>(null);
  readonly link = signal(false);
  asked = 0;
}

describe('FrameFileGuide', () => {
  const fields: FormField[] = [
    { name: 'start', xpath: 'start', type: 'start', label: null },
    { name: 'idiomas', xpath: 'idiomas', type: 'select_multiple', label: 'Idiomas', listName: 'idiomas' },
    { name: 'estrato', xpath: 'estrato', type: 'integer', label: 'Estrato' },
    { name: 'municipio', xpath: 'geo/municipio', type: 'select_one', label: 'Municipio', listName: 'municipios' },
    { name: 'municipio', xpath: 'info/municipio', type: 'note', label: 'Sobre el municipio' },
  ];
  const choices: FormChoice[] = [
    { listName: 'idiomas', name: 'es', label: 'Español' },
    { listName: 'municipios', name: '05001', label: 'Medellín' },
    { listName: 'municipios', name: '05088', label: 'Bello' },
    { listName: 'municipios', name: '05266', label: 'Envigado' },
  ];

  async function render() {
    TestBed.configureTestingModule({ imports: [translocoTesting()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const table = () => [...element.querySelectorAll('[data-testid="guide-example"] tr')].map((row) => [...row.children].map((cell) => cell.textContent!.trim()));
    return { fixture, host: fixture.componentInstance, element, table };
  }

  it('shows a fixed example while the choices are not loaded', async () => {
    const { host, fixture, table } = await render();
    host.fields.set(fields);
    await fixture.whenStable();

    expect(table()).toEqual([
      ['municipio', '# municipio label', 'sexo', 'target'],
      ['05001', 'Medellín', 'f', '120'],
      ['05001', 'Medellín', 'm', '110'],
      ['05088', 'Bello', 'f', ''],
    ]);
  });

  it('builds the example from the first question with choices once they are loaded', async () => {
    const { host, fixture, table } = await render();
    host.fields.set(fields);
    host.choices.set(choices);
    await fixture.whenStable();

    // The multiple-choice question is skipped, and the note sharing the name makes the column take the path.
    expect(table()).toEqual([
      ['geo/municipio', '# geo/municipio label', 'target'],
      ['05001', 'Medellín', '120'],
      ['05088', 'Bello', ''],
    ]);
  });

  it('states the rules with the limits of the shared constants', async () => {
    const { element } = await render();

    const rules = element.querySelector('[data-testid="guide-rules"]')!.textContent!;
    expect(rules).toContain('1,000,000');
    expect(rules).toContain('# are notes');
    expect(rules).toContain('target is empty is left out');
    expect(rules).toContain('05001 as text');
    expect(rules).toContain('only the first sheet');
    expect(element.querySelector('[data-testid="guide-limits"]')!.textContent).toBe(
      `At most ${maxFrameQuestions} dimensions, 10,000 rows and ${maxFileMegabytes} MB.`,
    );
  });

  it('offers the template link only where asked, and reports it', async () => {
    const { host, fixture, element } = await render();
    expect(element.querySelector('[data-testid="guide-template"]')).toBeNull();

    host.link.set(true);
    await fixture.whenStable();
    (element.querySelector('[data-testid="guide-template"]') as HTMLButtonElement).click();

    expect(host.asked).toBe(1);
  });
});
