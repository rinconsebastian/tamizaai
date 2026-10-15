import { FormChoice, FormField } from '../../core/projects/projects.service';
import {
  choiceList,
  choiceLists,
  chosenCodes,
  combinationCount,
  combinations,
  guessField,
  hasChoices,
  normalizeValues,
  repeatedHeader,
  templateHeaders,
  templateIssue,
} from './sampling-frame-fields';

const last = (xpath: string) => xpath.split('/').at(-1)!;
const selectOne = (xpath: string, listName: string, extra: Partial<FormField> = {}): FormField => ({
  name: last(xpath),
  xpath,
  type: 'select_one',
  label: xpath,
  listName,
  ...extra,
});
const typed = (xpath: string, type = 'integer'): FormField => ({ name: last(xpath), xpath, type, label: xpath });
const choicesOf = (listName: string, ...codes: string[]): FormChoice[] => codes.map((name) => ({ listName, name, label: `Label ${name}` }));

/** What the API's SamplingFrameTemplate.Plan gives for a request, built from the shared helpers as the picker builds it. */
function plan(snapshot: FormField[], choices: FormChoice[], dimensions: { field: string; values: string[] | null }[]) {
  const lists = choiceLists(choices);
  const values = dimensions.map(({ field: xpath, values: chosen }) => {
    const field = snapshot.find((f) => f.xpath === xpath)!;
    const list = choiceList(field, lists);
    if (!list) {
      return normalizeValues(chosen ?? []);
    }
    return chosen === null ? lists.get(field.listName!)!.map((c) => c.code) : chosenCodes(list, chosen);
  });
  const headers = templateHeaders(snapshot, dimensions.map((d) => d.field));
  return { headers, values, count: combinationCount(values.map((v) => v.length)), rows: combinations(values) };
}

describe('sampling frame template rules', () => {
  // The cases of SamplingFrameTemplateTests (headers, counts and first rows).

  it('names a grouped target question after its path', () => {
    const result = plan([selectOne('G/Target', 's')], choicesOf('s', 'f', 'm'), [{ field: 'G/Target', values: null }]);

    expect(result.headers).toEqual(['G/Target']);
    expect(result.count).toBe(2);
    expect(result.rows[0]).toEqual(['f']);
  });

  it('gives the path when a note shares the name', () => {
    const snapshot = [selectOne('hogar/edad', 'e'), { name: 'edad', xpath: 'info/edad', type: 'note', label: 'Sobre la edad' }, typed('sexo')];

    const result = plan(snapshot, choicesOf('e', '1', '2'), [
      { field: 'hogar/edad', values: null },
      { field: 'sexo', values: ['f'] },
    ]);

    expect(result.headers).toEqual(['hogar/edad', 'sexo']);
    expect(result.count).toBe(2);
    expect(result.rows[0]).toEqual(['1', 'f']);
  });

  it('keeps codes that differ only in case once', () => {
    const snapshot = [selectOne('letra', 'l')];
    const choices = choicesOf('l', 'A', 'a');

    const all = plan(snapshot, choices, [{ field: 'letra', values: null }]);
    const chosen = plan(snapshot, choices, [{ field: 'letra', values: ['a'] }]);

    expect(all.count).toBe(1);
    expect(all.rows).toEqual([['A']]);
    expect(chosen.rows).toEqual([['A']]);
  });

  it('offers other on an or_other question without choosing it by default', () => {
    const snapshot = [selectOne('sexo', 's', { orOther: true })];
    const choices = choicesOf('s', 'f', 'm');

    const all = plan(snapshot, choices, [{ field: 'sexo', values: null }]);
    const explicitly = plan(snapshot, choices, [{ field: 'sexo', values: ['f', 'm', 'other'] }]);

    const list = choiceList(snapshot[0], choiceLists(choices))!;
    expect(list.map((c) => c.code)).toEqual(['f', 'm', 'other']);
    expect(list[2].label).toBe('Other');
    expect(all.values[0]).toEqual(['f', 'm']);
    expect(all.count).toBe(2);
    expect(explicitly.values[0]).toEqual(['f', 'm', 'other']);
    expect(explicitly.count).toBe(3);
  });

  it('adds nothing to an or_other question whose list has other', () => {
    const snapshot = [selectOne('sexo', 's', { orOther: true })];

    const all = plan(snapshot, choicesOf('s', 'f', 'Other'), [{ field: 'sexo', values: null }]);

    expect(choiceList(snapshot[0], choiceLists(choicesOf('s', 'f', 'Other')))!.map((c) => c.code)).toEqual(['f', 'Other']);
    expect(all.values[0]).toEqual(['f', 'Other']);
    expect(all.count).toBe(2);
  });

  it('trims typed values and keeps each once', () => {
    expect(normalizeValues('1\n 1\n\n2\n')).toEqual(['1', '2']);
    const result = plan([typed('estrato')], [], [{ field: 'estrato', values: '1\n 1\n\n2\n'.split('\n') }]);
    expect(result.values[0]).toEqual(['1', '2']);
    expect(result.count).toBe(2);
    expect(result.rows[0]).toEqual(['1']);
  });

  it('gives no rows when every chosen list is empty', () => {
    const result = plan([typed('barrio', 'text'), typed('estrato')], [], [
      { field: 'barrio', values: [] },
      { field: 'estrato', values: null },
    ]);

    expect(result.count).toBe(0);
    expect(result.rows).toEqual([]);
  });

  // Further rules

  it('combines in order with the first column slowest and empty columns blank', () => {
    expect(combinations([['1', '2'], [], ['x', 'y']])).toEqual([
      ['1', '', 'x'],
      ['1', '', 'y'],
      ['2', '', 'x'],
      ['2', '', 'y'],
    ]);
    expect(combinationCount([5, 0, 3])).toBe(15);
    expect(combinationCount([1100, 2, 5])).toBe(11_000);
  });

  it('only offers lists of select_one questions that are not from a file', () => {
    const lists = choiceLists(choicesOf('l', 'a'));

    expect(hasChoices(selectOne('q', 'l'), lists)).toBe(true);
    expect(hasChoices({ ...selectOne('q', 'l'), type: 'select_one_from_file fincas.csv' }, lists)).toBe(false);
    expect(hasChoices({ ...selectOne('q', 'l'), type: 'select_multiple' }, lists)).toBe(false);
    expect(hasChoices(selectOne('q', 'empty'), lists)).toBe(false);
  });

  it('says why a question is not offered', () => {
    expect(templateIssue({ ...typed('idiomas'), type: 'select_multiple_idiomas' })).toBe('multiple');
    expect(templateIssue({ ...typed('miembros/edad'), inRepeat: true })).toBe('repeat');
    expect(templateIssue(typed('Target'))).toBe('target');
    expect(templateIssue(typed('hogar/target'))).toBeNull();
  });

  it('finds headers repeated ignoring case', () => {
    expect(repeatedHeader(templateHeaders([typed('G/edad'), typed('g/edad')], ['G/edad', 'g/edad']))).toBe('g/edad');
    expect(repeatedHeader(['a', 'b'])).toBeNull();
  });
});

describe('guessField', () => {
  const snapshot: FormField[] = [
    { name: 'start', xpath: 'start', type: 'start', label: null },
    selectOne('municipio', 'm', { label: 'Municipio' }),
    selectOne('hogar/edad', 'e', { label: 'Edad' }),
    { name: 'edad', xpath: 'info/edad', type: 'note', label: 'Sobre la edad' },
    selectOne('G/Target', 's', { label: 'Meta' }),
    typed('estrato'),
  ];
  const fields = snapshot.filter((f) => f.type !== 'note' && f.type !== 'start');

  it('preselects the chosen question for every header a template gives', () => {
    const xpaths = ['municipio', 'hogar/edad', 'G/Target', 'estrato'];

    const headers = templateHeaders(snapshot, xpaths);

    expect(headers).toEqual(['municipio', 'hogar/edad', 'G/Target', 'estrato']);
    expect(headers.map((header) => guessField(fields, header).field)).toEqual(xpaths);
  });

  it('preselects the saved field for every header of a downloaded frame', () => {
    const saved = [
      { name: 'Municipality', field: 'municipio' },
      { name: 'Age', field: 'hogar/edad' },
      { name: '#zona', field: 'estrato' },
    ];
    // The download names #zona after its path, as the API does.
    const headers = ['Municipality', 'Age', 'estrato'];

    expect(headers.map((header) => guessField(fields, header, saved).field)).toEqual(['municipio', 'hogar/edad', 'estrato']);
  });

  it('Re-imported frame keeps its mapping', () => {
    const form = [selectOne('geo/municipio', 'm')];

    const guess = guessField(form, 'Municipality', [{ name: 'municipality', field: 'geo/municipio' }]);

    expect(guess).toEqual({ field: 'geo/municipio', conflict: null });
  });

  it('Saved question no longer in the form', () => {
    const form = [selectOne('lugar/municipality', 'm')];

    expect(guessField(form, 'Municipality', [{ name: 'municipality', field: 'geo/municipio' }]).field).toBe('lugar/municipality');
  });

  it('Column named after a path', () => {
    const form = [typed('hogar/sexo'), typed('jefe/sexo')];

    expect(guessField(form, 'hogar/sexo').field).toBe('hogar/sexo');
    expect(guessField(form, 'HOGAR/SEXO').field).toBe('hogar/sexo');
  });

  it('Name shared by two questions', () => {
    const form = [typed('hogar/edad'), typed('jefe/edad')];

    expect(guessField(form, 'edad')).toEqual({ field: '', conflict: null });
  });

  it('Saved mapping disagrees with the form', () => {
    const form = [typed('sexo'), typed('jefe/sexo_jefe')];

    const guess = guessField(form, 'sexo', [{ name: 'sexo', field: 'jefe/sexo_jefe' }]);

    expect(guess).toEqual({ field: '', conflict: { saved: 'jefe/sexo_jefe', form: 'sexo' } });
  });

  it('falls back to the only question with that name or label', () => {
    expect(guessField(fields, 'edad').field).toBe('hogar/edad');
    expect(guessField(fields, 'Municipio').field).toBe('municipio');
    expect(guessField(fields, 'nothing').field).toBe('');
  });
});
