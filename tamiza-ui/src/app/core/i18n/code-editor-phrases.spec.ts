import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';
import { codeEditorPhrases } from './code-editor-phrases';

const lookup = (tree: object) => (key: string) =>
  key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)?.[part], tree) as string;

// That the list covers every phrase the installed @codemirror packages ask for is checked by `npm run check:safety`: a spec has
// no file system to read the packages with.
describe('CodeMirror phrases', () => {
  it.each([
    ['en', en],
    ['es', es],
  ])('has a text in %s for every phrase', (_lang, tree) => {
    const phrases = codeEditorPhrases(lookup(tree));

    for (const [phrase, text] of Object.entries(phrases)) {
      expect(typeof text, phrase).toBe('string');
      expect(text.trim(), phrase).not.toBe('');
    }
  });

  it('repeats each phrase as it is in the English file, because CodeMirror looks a phrase up by its English wording', () => {
    for (const [phrase, text] of Object.entries(codeEditorPhrases(lookup(en)))) {
      expect(text).toBe(phrase);
    }
  });
});
