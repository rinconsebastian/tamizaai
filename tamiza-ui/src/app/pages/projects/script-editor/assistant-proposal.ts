import type { StoredProposal } from '../../../core/projects/assistant-storage';
import type { ScriptKind } from '../../../core/projects/pipeline.service';
import { ProposalBlockView, ProposalMark, ProposalNote, ProposalOffer, ProposalView, cellReplacement, findCell } from './assistant-state';
import { cells, hasContract } from './script-analysis';

/** The key of the check that says a text does not define the contract function; a fragment is not expected to. */
const noContractKey = 'scripts.checks.noContract';

/** The editor's checks on a text, as marks with their lines (the page-scoped session supplies them from `lintProblems`). */
export type CheckText = (text: string, ignore?: string) => ProposalMark[];

/** The number of lines of a block of code, not counting the line break that ends it. */
export function countLines(code: string): number {
  return code.replace(/\r?\n$/, '').split('\n').length;
}

/** Whether a block is one `# %%` cell: its first non-blank line is a marker and it holds no other. */
function isOneCell(code: string): boolean {
  const all = cells(code);
  const first = code.split('\n').findIndex((line) => line.trim() !== '');
  return all.length === 1 && first >= 0 && all[0].line === first + 1;
}

/**
 * Classifies an answer's code into the actions the tab offers (design D8) and runs the editor's checks on what the editor would
 * hold after applying each block. `draft` is the editor's text now: a cell is found again by its text as it was sent, so a line
 * added above does not matter and an edited cell withdraws Replace cell.
 *
 * - One complete block of a script or fix answer that defines the contract function: Replace script and Insert at cursor.
 * - One complete block of a cell answer that is one cell, while exactly one cell of the editor still has the targeted text:
 *   Replace cell and Insert at cursor.
 * - Anything else is offered for insertion only, with the note that says why.
 */
export function classifyProposal(answer: StoredProposal, kind: ScriptKind, draft: string, check: CheckText): ProposalView {
  const notes: ProposalNote[] = [];
  const note = (value: ProposalNote) => {
    if (!notes.includes(value)) {
      notes.push(value);
    }
  };
  const several = answer.blocks.length > 1;
  if (several) {
    note(answer.action === 'cell' ? 'notOneCell' : 'notOneScript');
  }
  if (answer.blocks.some((block) => !block.complete)) {
    note('cutOff');
  }

  const blocks = answer.blocks.map((block): ProposalBlockView => {
    const definesContract = hasContract(block.code, kind);
    let offers: ProposalOffer[] = ['insert'];
    let after: string | null = null;
    if (!several && block.complete) {
      if (answer.action === 'cell') {
        if (!isOneCell(block.code)) {
          note('notOneCell');
        } else {
          const range = answer.cell ? findCell(draft, answer.cell.text) : null;
          if (range) {
            offers = ['cell', 'insert'];
            after = draft.slice(0, range.from) + cellReplacement(block.code, range.text) + draft.slice(range.to);
          } else {
            note('cellChanged');
          }
        }
      } else if (definesContract) {
        offers = ['script', 'insert'];
        after = block.code;
      }
    }
    // A block that replaces nothing is a fragment: it is checked alone, and not expected to define the contract function.
    const marks = after === null ? check(block.code, noContractKey) : check(after);
    return { lines: countLines(block.code), definesContract, offers, marks };
  });
  return { callId: answer.callId, blocks, notes };
}
