import { DialogRef } from '@angular/cdk/dialog';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import es from '../../../public/i18n/es.json';
import { InterfaceLanguage } from '../core/i18n/interface-language';
import { McpConnection } from '../core/projects/mcp.service';
import { translocoTesting } from '../../testing/transloco-testing';
import { ConnectAiClientDialog, claudeCodeCommand } from './connect-ai-client-dialog';

const textOf = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const byTestId = <T extends HTMLElement = HTMLElement>(root: ParentNode, id: string) => root.querySelector<T>(`[data-testid="${id}"]`);

/** What the deployment gives with the defaults, on `https://tamiza.example.org`. */
const defaults: McpConnection = {
  url: 'https://tamiza.example.org/mcp',
  clientId: 'tamiza-mcp',
  callbackPort: 33418,
  maxDataAccess: 'aggregates',
  accountUrl: 'https://kc.example.org/realms/tamiza/account/#/applications',
};

describe('ConnectAiClientDialog', () => {
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;
  let fixture: ComponentFixture<ConnectAiClientDialog>;
  let root: HTMLElement;

  async function settle(): Promise<void> {
    for (let i = 0; i < 3; i++) {
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  async function open(answer: McpConnection | { status: number } = defaults, lang: InterfaceLanguage = 'en') {
    close = vi.fn();
    TestBed.configureTestingModule({
      imports: [translocoTesting(lang)],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DialogRef, useValue: { close } }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConnectAiClientDialog);
    root = fixture.nativeElement as HTMLElement;
    await settle();
    const request = http.expectOne('/api/v1/mcp');
    if ('status' in answer) {
      request.flush(null, { status: answer.status, statusText: 'Error' });
    } else {
      request.flush(answer);
    }
    await settle();
  }

  const clipboard = (writeText: () => Promise<void>) => Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });

  afterEach(() => http.verify());

  describe('the details', () => {
    it('shows the MCP address, the client ID and the Claude Code command for the defaults (Claude Code command)', async () => {
      await open();

      expect(textOf(byTestId(root, 'connect-address'))).toBe('https://tamiza.example.org/mcp');
      expect(textOf(byTestId(root, 'connect-client-id'))).toBe('tamiza-mcp');
      expect(textOf(byTestId(root, 'connect-command'))).toBe(
        'claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 tamiza https://tamiza.example.org/mcp',
      );
    });

    it("builds the command from the deployment's own client, port and address", async () => {
      const own: McpConnection = { ...defaults, url: 'https://data.example.org/mcp', clientId: 'field-mcp', callbackPort: 40000 };
      await open(own);

      expect(textOf(byTestId(root, 'connect-command'))).toBe(
        'claude mcp add --transport http --client-id field-mcp --callback-port 40000 tamiza https://data.example.org/mcp',
      );
      expect(claudeCodeCommand(own)).toBe(textOf(byTestId(root, 'connect-command')));
    });

    it('gives the steps for claude.ai and Claude Desktop with the client ID and no secret', async () => {
      await open();

      const steps = [...root.querySelectorAll('[data-testid="connect-claude-ai-steps"] li')].map(textOf);
      expect(textOf(root.querySelector('#connect-claude-ai-title'))).toBe('claude.ai and Claude Desktop');
      expect(steps).toHaveLength(4);
      expect(steps[0]).toContain('Customize');
      expect(steps[0]).toContain('Connectors');
      expect(steps[0]).toContain('Add custom connector');
      expect(steps[1]).toContain('paste the MCP address');
      expect(steps[2]).toContain('OAuth client');
      expect(steps[2]).toContain('client ID');
      expect(steps[2]).toContain('leave the client secret empty');
    });

    it('tells Claude Code users to run /mcp to sign in, after the command', async () => {
      await open();

      const section = root.querySelector('section[aria-labelledby="connect-claude-code-title"]')!;
      expect(textOf(section.querySelector('h3'))).toBe('Claude Code');
      expect(textOf(section)).toContain('run /mcp and choose tamiza to sign in');
      expect(section.contains(byTestId(root, 'connect-command'))).toBe(true);
    });

    it('links to the account console to revoke a connection, in a new tab without a referrer', async () => {
      await open();

      const link = byTestId<HTMLAnchorElement>(root, 'connect-revoke-link')!;
      expect(link.getAttribute('href')).toBe(defaults.accountUrl);
      expect(link.getAttribute('target')).toBe('_blank');
      expect(link.getAttribute('rel')).toBe('noopener noreferrer');
      expect(textOf(root.querySelector('#connect-revoke-title'))).toBe('Revoke a connection');
      expect(textOf(root.querySelector('section[aria-labelledby="connect-revoke-title"]'))).toContain('remove the connector in Claude');
    });

    it('says that only projects whose admin allowed AI clients can be reached, at the level the project shows', async () => {
      await open();

      const note = textOf(byTestId(root, 'connect-projects-note'));
      expect(note).toContain('Only projects whose admin allowed AI clients can be reached');
      expect(note).toContain('the level the project shows in Settings > General');
    });

    it('is named by its title for screen readers', async () => {
      await open();

      expect(root.querySelector('#connect-ai-client-title')?.textContent).toBe('Connect an AI client');
    });
  });

  describe('copying', () => {
    it.each([
      ['copy-address', 'https://tamiza.example.org/mcp', 'Copy the MCP address'],
      ['copy-client-id', 'tamiza-mcp', 'Copy the client ID'],
      [
        'copy-command',
        'claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 tamiza https://tamiza.example.org/mcp',
        'Copy the command',
      ],
    ])('%s puts its value on the clipboard and says so', async (testId, value, label) => {
      const writeText = vi.fn(() => Promise.resolve());
      clipboard(writeText);
      await open();
      const button = byTestId<HTMLButtonElement>(root, testId)!;

      expect(button.getAttribute('aria-label')).toBe(label);
      expect(button.tagName).toBe('BUTTON');
      button.click();
      await settle();

      expect(writeText).toHaveBeenCalledWith(value);
      expect(textOf(byTestId(root, 'connect-copy-status'))).toBe('Copied to the clipboard.');
      expect(byTestId(root, 'connect-copy-status')?.getAttribute('role')).toBe('status');
    });

    it('says when the browser refuses, and asks to copy by hand', async () => {
      clipboard(() => Promise.reject(new Error('denied')));
      await open();

      byTestId<HTMLButtonElement>(root, 'copy-address')!.click();
      await settle();

      expect(textOf(byTestId(root, 'connect-copy-status'))).toBe('Could not copy. Select the text and copy it manually.');
    });

    it('clears the message after a few seconds', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      try {
        clipboard(() => Promise.resolve());
        await open();
        byTestId<HTMLButtonElement>(root, 'copy-client-id')!.click();
        await settle();
        expect(textOf(byTestId(root, 'connect-copy-status'))).toBe('Copied to the clipboard.');

        vi.advanceTimersByTime(4000);
        await settle();

        expect(textOf(byTestId(root, 'connect-copy-status'))).toBe('');
      } finally {
        vi.useRealTimers();
      }
    });
  });

  describe('keyboard and closing', () => {
    it('has only real buttons and a link to act on, in reading order, so Tab reaches each of them', async () => {
      await open();

      const actionable = [...root.querySelectorAll<HTMLElement>('button, a[href]')].map((element) => element.dataset['testid']);
      expect(actionable).toEqual(['connect-x', 'copy-address', 'copy-client-id', 'copy-command', 'connect-revoke-link', 'connect-close']);
      for (const element of root.querySelectorAll<HTMLElement>('button, a[href]')) {
        expect(element.tabIndex).toBeGreaterThanOrEqual(0);
      }
    });

    it('closes from the X and from the Close button', async () => {
      await open();

      byTestId<HTMLButtonElement>(root, 'connect-x')!.click();
      byTestId<HTMLButtonElement>(root, 'connect-close')!.click();

      expect(close).toHaveBeenCalledTimes(2);
      expect(byTestId(root, 'connect-x')?.getAttribute('aria-label')).toBe('Close');
    });
  });

  describe('when the details are not there', () => {
    it('shows a placeholder while they load and offers no copy button', async () => {
      close = vi.fn();
      TestBed.configureTestingModule({
        imports: [translocoTesting()],
        providers: [provideHttpClient(), provideHttpClientTesting(), { provide: DialogRef, useValue: { close } }],
      });
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(ConnectAiClientDialog);
      root = fixture.nativeElement as HTMLElement;
      await settle();

      expect(byTestId(root, 'connect-loading')?.getAttribute('aria-busy')).toBe('true');
      expect(root.querySelectorAll('[data-testid^="copy-"]')).toHaveLength(0);
      http.expectOne('/api/v1/mcp').flush(defaults);
    });

    it('says AI clients are not enabled when the API answers 404 (Disabled)', async () => {
      await open({ status: 404 });

      expect(textOf(byTestId(root, 'connect-error'))).toBe('AI clients are not enabled in this installation.');
      expect(byTestId(root, 'connect-address')).toBeNull();
      expect(root.querySelectorAll('[data-testid^="copy-"]')).toHaveLength(0);
    });

    it('says the details could not be loaded after any other failure', async () => {
      await open({ status: 500 });

      expect(textOf(byTestId(root, 'connect-error'))).toBe('The connection details could not be loaded. Close this window and try again.');
      expect(byTestId(root, 'connect-command')).toBeNull();
    });
  });

  describe('on a narrow screen', () => {
    // jsdom has no layout, so this checks the classes that keep the dialog inside a 390 px screen; the stack verification looks at it in Chrome.
    it('fits the width of the screen: the panel is capped at the viewport, the body scrolls on its own, long values wrap', async () => {
      await open({ ...defaults, url: `https://tamiza.example.org/${'a'.repeat(120)}/mcp` });

      const panel = byTestId(root, 'connect-dialog')!;
      expect(panel.className).toContain('w-[min(36rem,calc(100vw-2rem))]');
      expect(panel.className).toContain('max-h-[calc(100dvh-2rem)]');
      expect(panel.querySelector('.overflow-y-auto')).not.toBeNull();
      const values = [...root.querySelectorAll('code')];
      expect(values).toHaveLength(3);
      for (const value of values) {
        expect(value.className).toContain('break-all');
        expect(value.className).toContain('min-w-0');
      }
      expect(panel.innerHTML).not.toMatch(/whitespace-nowrap|overflow-x-scroll|min-w-\[|w-\[\d+px\]/);
      expect(byTestId(root, 'connect-revoke-link')?.className).toContain('break-all');
    });

    it('puts a value and its copy button in a column below the small breakpoint', async () => {
      await open();

      const row = byTestId(root, 'connect-address')!.parentElement!;
      expect(row.className).toContain('flex-col');
      expect(row.className).toContain('sm:flex-row');
    });
  });

  it('is translated: the Spanish text, with the same command', async () => {
    await open(defaults, 'es');

    expect(root.querySelector('#connect-ai-client-title')?.textContent).toBe(es.connect.title);
    expect(textOf(root.querySelector('#connect-revoke-title'))).toBe(es.connect.revoke.title);
    expect(byTestId(root, 'copy-address')?.getAttribute('aria-label')).toBe(es.connect.copyAddress);
    expect(textOf(byTestId(root, 'connect-projects-note'))).toBe(es.connect.projects);
    expect(textOf(byTestId(root, 'connect-command'))).toBe(
      'claude mcp add --transport http --client-id tamiza-mcp --callback-port 33418 tamiza https://tamiza.example.org/mcp',
    );
  });
});
