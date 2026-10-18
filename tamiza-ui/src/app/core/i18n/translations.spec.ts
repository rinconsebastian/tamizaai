import en from '../../../../public/i18n/en.json';
import es from '../../../../public/i18n/es.json';

type Tree = { [key: string]: string | Tree };

function leaves(tree: Tree, prefix = ''): Map<string, string> {
  const result = new Map<string, string>();
  for (const [key, value] of Object.entries(tree)) {
    if (typeof value === 'string') {
      result.set(prefix + key, value);
    } else {
      leaves(value, `${prefix}${key}.`).forEach((text, path) => result.set(path, text));
    }
  }
  return result;
}

const params = (text: string) => [...new Set([...text.matchAll(/\{\{\s*([\w.]+)\s*\}\}/g)].map((match) => match[1]))].sort();
const edges = (text: string) => ({ start: /^\s*/.exec(text)![0], end: /\s*$/.exec(text)![0] });

/** What is wrong with `other` as a translation of `base`: missing or extra keys, parameters, edge spaces, empty text. */
function problems(base: Tree, other: Tree): string[] {
  const from = leaves(base);
  const to = leaves(other);
  const found: string[] = [];

  for (const [key, text] of from) {
    const translated = to.get(key);
    if (translated === undefined) {
      found.push(`${key}: missing`);
      continue;
    }
    if (translated.trim() === '') {
      found.push(`${key}: empty`);
    }
    if (params(text).join() !== params(translated).join()) {
      found.push(`${key}: parameters ${params(text).join(',')} became ${params(translated).join(',')}`);
    }
    const a = edges(text);
    const b = edges(translated);
    if (a.start !== b.start || a.end !== b.end) {
      found.push(`${key}: leading or trailing spaces differ`);
    }
  }
  for (const key of to.keys()) {
    if (!from.has(key)) {
      found.push(`${key}: not in the base language`);
    }
  }

  return found;
}

const copyOfEnglish = () => structuredClone(en) as unknown as Tree;

describe('translation files', () => {
  it('es.json has every key of en.json, with the same parameters and edge spaces', () => {
    expect(problems(en as unknown as Tree, es as unknown as Tree)).toEqual([]);
  });

  it('en.json has every key of es.json', () => {
    expect(problems(es as unknown as Tree, en as unknown as Tree)).toEqual([]);
  });

  describe('the check itself', () => {
    it('reports a missing key', () => {
      const broken = copyOfEnglish();
      delete (broken['shell'] as Tree)['signOut'];

      expect(problems(en as unknown as Tree, broken)).toEqual(['shell.signOut: missing']);
    });

    it('reports a renamed parameter', () => {
      const broken = copyOfEnglish();
      const text = leaves(en as unknown as Tree).get('dashboards.nameDialog.copyN')!;
      (((broken['dashboards'] as Tree)['nameDialog'] as Tree)['copyN'] as string) = text.replace('{{ n }}', '{{ number }}');

      expect(problems(en as unknown as Tree, broken)).toEqual([
        'dashboards.nameDialog.copyN: parameters n became number',
      ]);
    });

    it('reports a dropped leading space', () => {
      const broken = copyOfEnglish();
      const nameDialog = (broken['dashboards'] as Tree)['nameDialog'] as Tree;
      nameDialog['copy'] = (nameDialog['copy'] as string).trimStart();

      expect(problems(en as unknown as Tree, broken)).toEqual(['dashboards.nameDialog.copy: leading or trailing spaces differ']);
    });

    it('reports an empty text and a key that the base language lacks', () => {
      const broken = copyOfEnglish();
      (broken['shell'] as Tree)['signOut'] = '';
      (broken['shell'] as Tree)['extra'] = 'Extra';

      expect(problems(en as unknown as Tree, broken)).toEqual(['shell.signOut: empty', 'shell.extra: not in the base language']);
    });
  });
});
