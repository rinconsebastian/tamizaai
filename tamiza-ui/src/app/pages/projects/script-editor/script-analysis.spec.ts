import type { ScriptKind } from '../../../core/projects/pipeline.service';
import { ScriptInputsInfo, cellAt, cells, check, parameterNames, parse, signatureOf, tableAliases } from './script-analysis';

const inputs: ScriptInputsInfo = {
  tables: [
    { name: 'main', rows: 3, columns: [{ name: 'municipality', type: 'text' }] },
    { name: 'members', rows: 6, columns: [{ name: 'age', type: 'integer' }] },
  ],
  referenceTables: [{ name: 'population', columns: [{ name: 'population_2026', type: 'integer' }] }],
  questions: [],
};

function problems(source: string, kind: ScriptKind = 'indicator', info: ScriptInputsInfo | null = inputs) {
  return check(source, parse(source), kind, info);
}

const keys = (source: string, kind: ScriptKind = 'indicator', info: ScriptInputsInfo | null = inputs) =>
  problems(source, kind, info).map((p) => p.key);

const good = 'def compute(cleaned, ctx):\n    return cleaned["main"]\n';

describe('script analysis: checks', () => {
  it('finds nothing in a script that follows the contract', () => {
    expect(problems(good)).toEqual([]);
  });

  it('says that script 2 must define prepare_output when it defines prepare', () => {
    const found = problems('def prepare(cleaned, ctx):\n    return {}\n', 'output');

    expect(found).toHaveLength(1);
    expect(found[0]).toMatchObject({ severity: 'error', key: 'scripts.checks.noContract', params: { kind: 'output', signature: 'prepare_output(cleaned, ctx)' } });
    expect(found[0].from).toBe(0);
  });

  it('marks a syntax error at the line where a parenthesis is left open, and only that', () => {
    const source = 'def compute(cleaned, ctx):\n    x = foo(1,\n    return cleaned["main"]\n    y = ctx.refrence\n';

    const found = problems(source);

    expect(found.length).toBeGreaterThan(0);
    expect(found.every((p) => p.key === 'scripts.checks.syntax' && p.severity === 'error')).toBe(true);
    expect(found.length).toBeLessThanOrEqual(3);
  });

  it('marks a missing colon', () => {
    expect(keys('def compute(cleaned, ctx)\n    return 1\n')).toContain('scripts.checks.syntax');
  });

  it('marks a member of ctx that does not exist, with the members it has', () => {
    const source = 'def compute(cleaned, ctx):\n    x = ctx.refrence\n    return cleaned["main"]\n';

    const [problem] = problems(source);

    expect(problem).toMatchObject({ severity: 'warning', key: 'scripts.checks.unknownMember', params: { object: 'ctx', member: 'refrence' } });
    expect(problem.params['members']).toBe('project_name, form, log, reference, show');
    expect(source.slice(problem.from, problem.to)).toBe('refrence');
  });

  it('marks a member of ctx.form and of ctx.log that do not exist', () => {
    const found = problems('def compute(cleaned, ctx):\n    ctx.form.lable("q", 1)\n    ctx.log.inf("x")\n    return cleaned["main"]\n');

    expect(found.map((p) => [p.params['object'], p.params['member']])).toEqual([
      ['ctx.form', 'lable'],
      ['ctx.log', 'inf'],
    ]);
  });

  it('accepts every member of the runner', () => {
    const source = [
      'def compute(cleaned, ctx):',
      '    ctx.project_name',
      '    ctx.form.questions; ctx.form.choices; ctx.form.columns',
      '    ctx.form.label("q", 1); ctx.form.name("q", "x"); ctx.form.column_label("main", "age")',
      '    ctx.log.info("x"); ctx.log.warning("x"); ctx.log.debug("x"); ctx.log.error("x")',
      '    ctx.reference.keys(); ctx.reference.get("population")',
      '    ctx.show(cleaned["main"], "main")',
      '    return ctx.form.questions.shape',
      '',
    ].join('\n');

    expect(problems(source)).toEqual([]);
  });

  it('uses the names the script gave its parameters', () => {
    const source = 'def compute(tables, context):\n    context.refrence\n    return tables["member"]\n';

    expect(keys(source)).toEqual(['scripts.checks.unknownMember', 'scripts.checks.unknownTable']);
    expect(parameterNames(parse(source), source, 'indicator')).toEqual(['tables', 'context']);
  });

  it('marks a reference table the project does not have and lists the ones it has', () => {
    const source = 'def compute(cleaned, ctx):\n    return ctx.reference["populaton"]\n';

    const [problem] = problems(source);

    expect(problem).toMatchObject({ key: 'scripts.checks.unknownReference', params: { name: 'populaton', available: 'population' } });
    expect(source.slice(problem.from, problem.to)).toBe('"populaton"');
  });

  it('accepts a reference table the project has, and says when it has none', () => {
    const source = 'def compute(cleaned, ctx):\n    return ctx.reference["population"]\n';

    expect(problems(source)).toEqual([]);
    expect(keys(source, 'indicator', { ...inputs, referenceTables: [] })).toEqual(['scripts.checks.noReference']);
  });

  it('marks a table the inputs do not have and lists the ones they have', () => {
    const [problem] = problems('def compute(cleaned, ctx):\n    return cleaned["member"]\n');

    expect(problem).toMatchObject({ key: 'scripts.checks.unknownTable', params: { name: 'member', available: 'main, members' } });
  });

  it('does not mark a table the script adds itself', () => {
    const source = 'def clean(raw, ctx):\n    raw["summary"] = raw["main"].head(1)\n    return {"main": raw["main"], "summary": raw["summary"]}\n';

    expect(problems(source, 'clean')).toEqual([]);
  });

  it('marks no table while no inputs are published, or before they are loaded', () => {
    const source = 'def compute(cleaned, ctx):\n    return cleaned["anything"]\n';

    expect(problems(source, 'indicator', { ...inputs, tables: [] })).toEqual([]);
    expect(problems(source, 'indicator', null)).toEqual([]);
  });

  it('does not check the columns of a table', () => {
    expect(problems('def compute(cleaned, ctx):\n    return cleaned["main"]["no_such_column"]\n')).toEqual([]);
  });

  it('warns about a contract function that does not take two parameters', () => {
    const [problem] = problems('def compute(cleaned, ctx, extra):\n    return cleaned["main"]\n');

    expect(problem).toMatchObject({ severity: 'warning', key: 'scripts.checks.parameters', params: { signature: 'compute(cleaned, ctx)' } });
    expect(keys('def compute(cleaned):\n    return cleaned["main"]\n')).toEqual(['scripts.checks.parameters']);
    expect(keys('def compute(cleaned, ctx=None):\n    return cleaned["main"]\n')).toEqual([]);
  });

  it('warns about a line indented with tabs and spaces, and about tabs in a script indented with spaces', () => {
    expect(keys('def compute(cleaned, ctx):\n \t return cleaned["main"]\n')).toEqual(['scripts.checks.mixedIndentation']);
    // A helper indented with tabs in a script whose other functions use spaces.
    const mixed = 'def helper():\n    return 1\n\n\ndef compute(cleaned, ctx):\n\treturn cleaned["main"]\n';
    expect(keys(mixed)).toEqual(['scripts.checks.mixedIndentation']);
    expect(keys('def compute(cleaned, ctx):\n\tx = 1\n\treturn cleaned["main"]\n')).toEqual([]);
  });

  it('names the kind in the signature of each script', () => {
    expect(signatureOf('clean')).toBe('clean(raw, ctx)');
    expect(signatureOf('output')).toBe('prepare_output(cleaned, ctx)');
    expect(signatureOf('indicator')).toBe('compute(cleaned, ctx)');
  });
});

describe('script analysis: imports', () => {
  const body = 'def compute(cleaned, ctx):\n    return cleaned["main"]\n';
  const marked = (source: string) => problems(source).filter((p) => p.key.endsWith('Import'));

  it('marks a library the worker does not have, naming it and listing the libraries that are', () => {
    const source = `from sklearn.linear_model import LinearRegression\n\n${body}`;

    const [problem] = marked(source);

    expect(problem).toMatchObject({ severity: 'warning', key: 'scripts.checks.unavailableImport', params: { module: 'sklearn' } });
    expect(problem.params['available']).toBe('duckdb, geopandas, numpy, openpyxl, pandas, polars, pyarrow, shapely');
    expect(source.slice(problem.from, problem.to)).toBe('sklearn.linear_model');
    expect(marked(`import scipy.stats\nimport statsmodels.api as sm\n\n${body}`).map((p) => p.params['module'])).toEqual(['scipy', 'statsmodels']);
  });

  it('marks nothing for the available libraries and the standard library', () => {
    const imports = 'import pandas as pd\nimport numpy as np\nimport geopandas\nimport shapely.geometry\nimport duckdb\nfrom datetime import date\n';

    expect(marked(`${imports}\n${body}`)).toEqual([]);
  });

  it('marks nothing for the standard library modules scripts commonly use', () => {
    expect(marked(`import re, math, json\nimport datetime\nimport unicodedata\n\n${body}`)).toEqual([]);
  });

  it('marks an import inside the contract function like one at the top', () => {
    const source = 'def compute(cleaned, ctx):\n    import requests\n    return cleaned["main"]\n';

    const [problem] = marked(source);

    expect(problem).toMatchObject({ severity: 'warning', key: 'scripts.checks.unavailableImport', params: { module: 'requests' } });
    expect(source.slice(problem.from, problem.to)).toBe('requests');
  });

  it('marks an import in a script that has no contract function', () => {
    expect(keys('import requests\n')).toEqual(['scripts.checks.noContract', 'scripts.checks.unavailableImport']);
  });

  it('marks modules a script never needs, and their submodules', () => {
    const source = `import subprocess\nfrom urllib.request import urlopen\nimport os.path\nimport os, pandas\nfrom http import client\n\n${body}`;

    const found = marked(source);

    expect(found.map((p) => p.key)).toEqual(Array(5).fill('scripts.checks.neverImport'));
    expect(found.map((p) => p.params['module'])).toEqual(['subprocess', 'urllib', 'os', 'os', 'http']);
    expect(found.every((p) => p.severity === 'warning')).toBe(true);
  });

  it('marks every module of one import statement on its own', () => {
    const source = `import pandas as pd, scipy as sp, os\n\n${body}`;

    expect(marked(source).map((p) => [p.key, source.slice(p.from, p.to)])).toEqual([
      ['scripts.checks.unavailableImport', 'scipy'],
      ['scripts.checks.neverImport', 'os'],
    ]);
  });

  it('marks a call of __import__', () => {
    const source = 'def compute(cleaned, ctx):\n    os = __import__("os")\n    return cleaned["main"]\n';

    const [problem] = marked(source);

    expect(problem).toMatchObject({ severity: 'warning', key: 'scripts.checks.neverImport' });
    expect(source.slice(problem.from, problem.to)).toBe('__import__("os")');
  });

  it('does not mark relative imports or star imports of available modules', () => {
    expect(marked(`from . import helpers\nfrom .helpers import x\nfrom pandas import *\n\n${body}`)).toEqual([]);
  });

  it('reports only the syntax error when the script cannot be read', () => {
    expect(keys('import requests\ndef compute(cleaned, ctx)\n    return 1\n')).toEqual(['scripts.checks.syntax']);
  });
});

describe('script analysis: table aliases', () => {
  it('finds the variables assigned from an input table, with or without a copy', () => {
    const source = [
      'def compute(cleaned, ctx):',
      '    main = cleaned["main"]',
      '    people = cleaned["members"].copy()',
      '    other = main.head()',
      '    return main',
      '',
    ].join('\n');

    expect([...tableAliases(parse(source), source, 'indicator')]).toEqual([
      ['main', 'main'],
      ['people', 'members'],
    ]);
  });

  it('uses the script\'s own name for the first parameter', () => {
    const source = 'def clean(tables, ctx):\n    m = tables["main"]\n    return tables\n';

    expect([...tableAliases(parse(source), source, 'clean')]).toEqual([['m', 'main']]);
  });
});

describe('script analysis: cells', () => {
  const script = [
    'import pandas as pd',
    '',
    'def compute(cleaned, ctx):',
    '    # %% Load',
    '    main = cleaned["main"]',
    '    # %%',
    '    rate = 1',
    '    # %%   Return the rate  ',
    '    return main',
    '',
  ].join('\n');

  it('lists the markers with their lines and names', () => {
    expect(cells(script).map((c) => [c.line, c.name])).toEqual([
      [4, 'Load'],
      [6, ''],
      [8, 'Return the rate'],
    ]);
  });

  it('gives the offsets of each marker line', () => {
    const [first] = cells(script);

    expect(script.slice(first.from, first.to)).toBe('    # %% Load');
  });

  it('does not take other comments or a marker after code for a cell', () => {
    expect(cells('x = 1  # %% not a cell\n#%% neither\n# % %\n')).toEqual([]);
  });

  it('finds the cell of a line and the line of the next marker', () => {
    const all = cells(script);

    expect(cellAt(all, 5)).toMatchObject({ index: 0, stopLine: 6 });
    expect(cellAt(all, 6)).toMatchObject({ index: 1, stopLine: 8 });
    expect(cellAt(all, 9)).toMatchObject({ index: 2, stopLine: null });
    expect(cellAt(all, 2)).toBeNull();
  });

  it('reads Windows line endings', () => {
    expect(cells('def f(a, b):\r\n    # %% Load\r\n    pass\r\n').map((c) => c.name)).toEqual(['Load']);
  });
});
