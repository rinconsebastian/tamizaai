import { DashboardFilter } from '../../../core/projects/indicators.service';
import { encodeFilters } from '../data-view';
import { activeFilters, appliedOf, fromDataFilter, isValueOf, kindOf, readAddress, writeAddress } from './dashboard-filters';

const municipality: DashboardFilter = { column: 'municipality', label: 'Municipality', kind: 'text', default: null };
const round: DashboardFilter = { column: 'round', label: 'Round', kind: 'number', default: { min: '3', max: '3' } };
const filters = [municipality, round];

describe('dashboard filter values', () => {
  it('start at the defaults when the address has none', () => {
    expect(readAddress(undefined, filters)).toEqual({ values: { municipality: null, round: { min: '3', max: '3' } }, dropped: [] });
  });

  it('are exactly what the address holds, so a cleared filter stays cleared despite its default', () => {
    expect(readAddress(encodeFilters([]), filters).values).toEqual({ municipality: null, round: null });
    expect(readAddress(encodeFilters([{ column: 'municipality', op: 'in', values: ['Cali'] }]), filters).values).toEqual({
      municipality: { values: ['Cali'] },
      round: null,
    });
  });

  it('drop an entry for a filter the dashboard no longer has, or whose value the kind cannot take, and name it', () => {
    const f = encodeFilters([
      { column: 'zone', op: 'in', values: ['north'] },
      { column: 'round', op: 'range', min: 'second' },
      { column: 'municipality', op: 'contains', value: 'Ca' },
    ]);

    expect(readAddress(f, filters)).toEqual({ values: { municipality: null, round: null }, dropped: ['zone', 'Round', 'Municipality'] });
  });

  it('write nothing to the address at the defaults, and every value otherwise', () => {
    expect(writeAddress(filters, { municipality: null, round: { min: '3', max: '3' } })).toBeNull();
    const written = writeAddress(filters, { municipality: { values: ['Cali'] }, round: null })!;
    expect(readAddress(written, filters).values).toEqual({ municipality: { values: ['Cali'] }, round: null });
    expect(readAddress(writeAddress(filters, { municipality: null, round: null }), filters).values).toEqual({ municipality: null, round: null });
  });

  it('send a widget\'s query the active filters it does not ignore', () => {
    const active = activeFilters(filters, { municipality: { values: ['Cali'] }, round: { min: '2', max: null } }, ['round']);

    expect(active.map((a) => [a.filter.label, a.ignored])).toEqual([
      ['Municipality', false],
      ['Round', true],
    ]);
    expect(appliedOf(active)).toEqual([{ column: 'municipality', kind: 'text', values: ['Cali'] }]);
  });
});

describe('dashboard filter kinds', () => {
  it('group the column types as the API does', () => {
    expect((['text', 'boolean', 'integer', 'decimal', 'date', 'timestamp'] as const).map(kindOf)).toEqual(['text', 'boolean', 'number', 'number', 'date', 'date']);
  });

  it('check a value as the API does', () => {
    expect(isValueOf('number', { min: '2.5' })).toBe(true);
    expect(isValueOf('number', { min: '-1e3' })).toBe(false);
    expect(isValueOf('number', { min: '3', max: '2' })).toBe(false);
    expect(isValueOf('date', { min: '2026-02-30' })).toBe(false);
    expect(isValueOf('date', { min: '2026-09-01', max: '2026-09-01' })).toBe(true);
    expect(isValueOf('boolean', { values: ['yes'] })).toBe(false);
    expect(isValueOf('text', { values: [] })).toBe(false);
    expect(isValueOf('text', { values: ['x'.repeat(501)] })).toBe(false);
  });

  it('read a clean data filter of the kind\'s condition only', () => {
    expect(fromDataFilter('text', { column: 'm', op: 'in', values: ['Cali'] })).toEqual({ values: ['Cali'] });
    expect(fromDataFilter('text', { column: 'm', op: 'equals', value: 'Cali' })).toBeNull();
    expect(fromDataFilter('number', { column: 'r', op: 'range', max: '4' })).toEqual({ min: null, max: '4' });
  });
});
