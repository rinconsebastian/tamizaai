import drop from './clean/drop-test-submissions.py';

describe('Python files imported as text', () => {
  it('gives the content of a template file', () => {
    expect(typeof drop).toBe('string');
    expect(drop).toContain('def clean(raw, ctx):');
    expect(drop).toContain('# %% Drop test submissions and duplicates');
  });
});
