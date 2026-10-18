/**
 * The texts CodeMirror writes itself (search panel, go to line, folding, diagnostics, completions), by the phrase it asks for, and
 * the translation key of each under `codeEditor.phrases`. CodeMirror looks a phrase up by its English wording, so the English
 * file repeats each phrase as it is. The list is taken from the `state.phrase(...)` calls of the installed `@codemirror/*`
 * packages; a spec checks that every key exists.
 */
export const codeEditorPhraseKeys: Readonly<Record<string, string>> = {
  Find: 'findField',
  Replace: 'replaceField',
  next: 'next',
  previous: 'previous',
  all: 'all',
  'match case': 'matchCase',
  regexp: 'regexp',
  'by word': 'byWord',
  replace: 'replaceButton',
  'replace all': 'replaceAll',
  close: 'close',
  'Go to line': 'goToLine',
  go: 'go',
  'current match': 'currentMatch',
  'on line': 'onLine',
  'replaced $ matches': 'replacedMatches',
  'replaced match on line $': 'replacedMatchOnLine',
  'folded code': 'foldedCode',
  to: 'to',
  unfold: 'unfold',
  'Folded lines': 'foldedLines',
  'Unfolded lines': 'unfoldedLines',
  'Fold line': 'foldLine',
  'Unfold line': 'unfoldLine',
  Diagnostics: 'diagnostics',
  'No diagnostics': 'noDiagnostics',
  Completions: 'completions',
  'Control character': 'controlCharacter',
  'Selection deleted': 'selectionDeleted',
};

/** The phrases to give the editor: each CodeMirror phrase mapped to its text in the interface language. */
export function codeEditorPhrases(translate: (key: string) => string): Record<string, string> {
  return Object.fromEntries(Object.entries(codeEditorPhraseKeys).map(([phrase, key]) => [phrase, translate(`codeEditor.phrases.${key}`)]));
}
