import { durationText } from './pipeline-runs.util';

const start = '2026-10-02T22:00:00Z';
const after = (seconds: number) => new Date(Date.parse(start) + seconds * 1000).toISOString();

describe('durationText', () => {
  it('writes seconds with one decimal in American English', () => {
    expect(durationText(start, after(8.24), 'en-US')).toBe('8.2 s');
    expect(durationText(start, after(8), 'en-US')).toBe('8.0 s');
  });

  it('writes the decimal comma of Colombian Spanish', () => {
    expect(durationText(start, after(8.24), 'es-CO')).toBe('8,2 s');
  });

  it('writes minutes and seconds from a minute on', () => {
    expect(durationText(start, after(185), 'en-US')).toBe('3 min 5 s');
    expect(durationText(start, after(185), 'es-CO')).toBe('3 min 5 s');
  });

  it('is null when either time is missing', () => {
    expect(durationText(null, after(5), 'en-US')).toBeNull();
    expect(durationText(start, null, 'es-CO')).toBeNull();
  });
});
