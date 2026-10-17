import type { StoredProposal } from '../../../core/projects/assistant-storage';
import { classifyProposal, countLines } from './assistant-proposal';
import { cellReplacement, findCell } from './assistant-state';

const cellScript = ['def clean(raw, ctx):', '    # %% Load', '    main = raw["main"]', '    # %% Rate', '    main["x"] = 1', '    # %% Out', '    return raw', ''].join('\n');
const rate = '    # %% Rate\n    main["x"] = 1';
const whole = 'def clean(raw, ctx):\n    return raw\n';

function answer(overrides: Partial<StoredProposal> = {}): StoredProposal {
  return { callId: 'c1', action: 'script', truncated: false, blocks: [{ code: whole, complete: true }], draft: cellScript, cell: null, ...overrides };
}

/** The checks, as a fake that records what it was asked about and marks every text it sees. */
function checker() {
  const seen: { text: string; ignore?: string }[] = [];
  return { seen, check: (text: string, ignore?: string) => (seen.push({ text, ignore }), [{ line: 1, severity: 'warning' as const, message: `checked ${text.length}` }]) };
}

describe('classifyProposal', () => {
  it('offers Replace script and Insert for one complete block that defines the contract function', () => {
    const { check, seen } = checker();
    const view = classifyProposal(answer(), 'clean', cellScript, check);

    expect(view.blocks[0]).toMatchObject({ lines: 2, definesContract: true, offers: ['script', 'insert'] });
    expect(view.notes).toEqual([]);
    // The checks run on what the editor would hold, the whole proposal.
    expect(seen).toEqual([{ text: whole, ignore: undefined }]);
    expect(view.blocks[0].marks).toEqual([{ line: 1, severity: 'warning', message: `checked ${whole.length}` }]);
  });

  it('uses the contract of the kind of script', () => {
    const view = classifyProposal(answer({ blocks: [{ code: whole, complete: true }] }), 'indicator', cellScript, checker().check);
    expect(view.blocks[0]).toMatchObject({ definesContract: false, offers: ['insert'] });
  });

  it('offers only Insert for code without the contract function, and checks it alone without asking for the contract', () => {
    const { check, seen } = checker();
    const view = classifyProposal(answer({ blocks: [{ code: 'rate = 1\n', complete: true }] }), 'clean', cellScript, check);

    expect(view.blocks[0].offers).toEqual(['insert']);
    expect(view.notes).toEqual([]);
    expect(seen).toEqual([{ text: 'rate = 1\n', ignore: 'scripts.checks.noContract' }]);
  });

  it('offers each of several blocks only for insertion, with a note for a script or a cell', () => {
    const two = [
      { code: whole, complete: true },
      { code: whole, complete: true },
    ];
    expect(classifyProposal(answer({ blocks: two }), 'clean', cellScript, checker().check)).toMatchObject({
      blocks: [{ offers: ['insert'] }, { offers: ['insert'] }],
      notes: ['notOneScript'],
    });
    expect(classifyProposal(answer({ action: 'fix', blocks: two }), 'clean', cellScript, checker().check).notes).toEqual(['notOneScript']);
    expect(classifyProposal(answer({ action: 'cell', cell: { text: rate }, blocks: two }), 'clean', cellScript, checker().check).notes).toEqual(['notOneCell']);
  });

  it('offers a block whose closing fence is missing only for insertion, with the cut-off note', () => {
    const view = classifyProposal(answer({ truncated: true, blocks: [{ code: 'def clean(raw, ctx):', complete: false }] }), 'clean', cellScript, checker().check);
    expect(view.blocks[0].offers).toEqual(['insert']);
    expect(view.notes).toEqual(['cutOff']);
  });

  it('offers no block when the answer has no code', () => {
    expect(classifyProposal(answer({ blocks: [] }), 'clean', cellScript, checker().check)).toEqual({ callId: 'c1', blocks: [], notes: [] });
  });

  describe('a cell answer', () => {
    const code = '    # %% Rate\n    main["x"] = 2';
    const cellAnswer = (overrides: Partial<StoredProposal> = {}) =>
      answer({ action: 'cell', cell: { text: rate }, blocks: [{ code, complete: true }], ...overrides });

    it('offers Replace cell while exactly one cell still has the targeted text, and checks the text with the cell replaced', () => {
      const { check, seen } = checker();
      const view = classifyProposal(cellAnswer(), 'clean', cellScript, check);

      expect(view.blocks[0].offers).toEqual(['cell', 'insert']);
      expect(view.notes).toEqual([]);
      expect(seen[0].text).toBe(cellScript.replace(rate, code));
    });

    it('finds the cell wherever it is now', () => {
      const moved = 'import pandas as pd\n' + cellScript;
      expect(classifyProposal(cellAnswer(), 'clean', moved, checker().check).blocks[0].offers).toEqual(['cell', 'insert']);
    });

    it('withdraws Replace cell when the cell was edited, lost its marker, or the text matches two cells', () => {
      const edited = cellScript.replace('= 1', '= 3');
      expect(classifyProposal(cellAnswer(), 'clean', edited, checker().check)).toMatchObject({ blocks: [{ offers: ['insert'] }], notes: ['cellChanged'] });
      const noMarker = cellScript.replace('    # %% Rate\n', '');
      expect(classifyProposal(cellAnswer(), 'clean', noMarker, checker().check).notes).toEqual(['cellChanged']);
      const duplicated = ['x = 1', rate, rate].join('\n');
      expect(classifyProposal(cellAnswer(), 'clean', duplicated, checker().check)).toMatchObject({ blocks: [{ offers: ['insert'] }], notes: ['cellChanged'] });
    });

    it('offers only Insert when the code is not one cell that starts with a marker', () => {
      for (const notOne of ['    main["x"] = 2', `${code}\n    # %% Other\n    y = 1`, whole]) {
        const view = classifyProposal(cellAnswer({ blocks: [{ code: notOne, complete: true }] }), 'clean', cellScript, checker().check);
        expect(view.blocks[0].offers).toEqual(['insert']);
        expect(view.notes).toEqual(['notOneCell']);
      }
    });

    it('accepts blank lines before the marker', () => {
      const view = classifyProposal(cellAnswer({ blocks: [{ code: `\n\n${code}`, complete: true }] }), 'clean', cellScript, checker().check);
      expect(view.blocks[0].offers).toEqual(['cell', 'insert']);
    });
  });
});

describe('countLines', () => {
  it('does not count the line break that ends the code', () => {
    expect(countLines('a')).toBe(1);
    expect(countLines('a\n')).toBe(1);
    expect(countLines('a\nb\n')).toBe(2);
    expect(countLines('a\n\nb')).toBe(3);
  });
});

describe('findCell and cellReplacement', () => {
  it('finds the range of a cell by its text, and nothing when it is missing or repeated', () => {
    const range = findCell(cellScript, rate)!;
    expect(cellScript.slice(range.from, range.to)).toBe(rate);
    expect(findCell(cellScript, '    # %% Gone')).toBeNull();
    expect(findCell([rate, rate].join('\n'), rate)).toBeNull();
  });

  it('keeps the blank lines a replaced cell ended with, and drops the ones the code brings', () => {
    expect(cellReplacement('\n# %% A\nx = 2\n\n', '# %% A\nx = 1\n\n')).toBe('# %% A\nx = 2\n\n');
    expect(cellReplacement('# %% A\nx = 2\n', '# %% A\nx = 1')).toBe('# %% A\nx = 2');
    expect(cellReplacement('# %% A\nx = 2', '# %% A\nx = 1\n')).toBe('# %% A\nx = 2\n');
  });
});
