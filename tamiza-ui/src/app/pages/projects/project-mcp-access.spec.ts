import { TestBed } from '@angular/core/testing';
import es from '../../../../public/i18n/es.json';
import { InterfaceLanguage } from '../../core/i18n/interface-language';
import { McpCall } from '../../core/projects/mcp.service';
import { ProjectDetails, ProjectRole } from '../../core/projects/projects.service';
import { SYSTEM_CONFIG_URL } from '../../core/system-config';
import { ToastService } from '../../shared/ui';
import { byTestId, details, openProjectSection, projectId, settle } from '../../../testing/project-harness';

const text = (element: Element | null) => element?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
const callsUrl = `/api/v1/projects/${projectId}/mcp-calls`;
const projectUrl = `/api/v1/projects/${projectId}`;

function configuration(mcp: boolean) {
  return {
    oidc: { authority: 'https://kc.example.org/realms/tamiza', clientId: 'tamiza-ui', scope: 'openid' },
    version: '0.1.0',
    assistant: { enabled: false },
    accountEmails: { enabled: false },
    email: { enabled: false },
    mcp: { enabled: mcp },
  };
}

function call(id: string, overrides: Partial<McpCall> = {}): McpCall {
  return {
    id,
    startedAt: '2026-10-28T15:04:00Z',
    userName: 'Ada Lovelace',
    tool: 'create_widget',
    status: 'succeeded',
    errorCode: null,
    access: 'aggregates',
    ...overrides,
  };
}

interface Seed {
  project?: Partial<ProjectDetails>;
  /** The calls the API lists; absent leaves the request unanswered. */
  calls?: McpCall[];
  lang?: InterfaceLanguage;
}

/** Opens General for a member, with the deployment's MCP server enabled, and answers the activity request unless the seed leaves it out. */
async function open(role: ProjectRole, seed: Seed = {}) {
  const opened = await openProjectSection('overview', role, seed.project, [], seed.lang);
  opened.http.expectOne(SYSTEM_CONFIG_URL).flush(configuration(true));
  await settle(opened.harness);
  if (seed.calls) {
    opened.http.expectOne(callsUrl).flush({ items: seed.calls });
    await settle(opened.harness);
  }
  return opened;
}

const radios = (root: HTMLElement) => [...root.querySelectorAll<HTMLInputElement>('[data-testid="mcp-form"] input[type="radio"]')];
const radio = (root: HTMLElement, level: string) => root.querySelector<HTMLInputElement>(`[data-testid="mcp-level-${level}"] input`)!;
const save = (root: HTMLElement) => byTestId<HTMLButtonElement>(root, 'mcp-save')!;

/** Picks an option the way a click does; jsdom needs the change event spelled out, as the project form's specs do. */
async function choose(opened: Awaited<ReturnType<typeof open>>, level: string) {
  const input = radio(opened.root, level);
  input.click();
  input.dispatchEvent(new Event('change'));
  await settle(opened.harness);
}

describe('ProjectMcpAccess', () => {
  describe('when the MCP server is not enabled', () => {
    it.each<ProjectRole>(['analyst', 'admin'])('draws no card for an %s and asks for no activity (Disabled)', async (role) => {
      const opened = await openProjectSection('overview', role);
      opened.http.expectOne(SYSTEM_CONFIG_URL).flush(configuration(false));
      await settle(opened.harness);

      expect(byTestId(opened.root, 'mcp-card')).toBeNull();
      expect(opened.root.textContent).not.toContain('AI clients');
      opened.http.expectNone(callsUrl);
    });

    it('draws no card while the configuration is unknown, nor when it cannot be read', async () => {
      const opened = await openProjectSection('overview', 'admin');
      expect(byTestId(opened.root, 'mcp-card')).toBeNull();

      opened.http.expectOne(SYSTEM_CONFIG_URL).flush(null, { status: 503, statusText: 'Service Unavailable' });
      await settle(opened.harness);

      expect(byTestId(opened.root, 'mcp-card')).toBeNull();
      opened.http.expectNone(callsUrl);
    });

    it('draws no card for an older API whose configuration has no mcp key', async () => {
      const opened = await openProjectSection('overview', 'admin');
      const { mcp: _mcp, ...older } = configuration(true);
      opened.http.expectOne(SYSTEM_CONFIG_URL).flush(older);
      await settle(opened.harness);

      expect(byTestId(opened.root, 'mcp-card')).toBeNull();
    });
  });

  describe('for an admin', () => {
    it('offers Off, Summaries only and Summaries and rows, each with what a client sees, and the stored one selected', async () => {
      const { root } = await open('admin', { project: { mcpAccess: 'aggregates', mcpMaxAccess: 'rows' }, calls: [] });

      expect(text(root.querySelector('[data-testid="mcp-card"] h2'))).toBe('AI clients (MCP)');
      expect(radios(root).map((r) => r.value)).toEqual(['off', 'aggregates', 'rows']);
      expect(radios(root).map((r) => r.checked)).toEqual([false, true, false]);
      expect(radios(root).every((r) => r.name === 'mcpAccess' && !r.disabled)).toBe(true);
      expect(text(root.querySelector('#mcp-level-off-title'))).toBe('Off');
      expect(text(root.querySelector('#mcp-level-off-description'))).toBe('AI clients see nothing of this project.');
      expect(text(root.querySelector('#mcp-level-aggregates-title'))).toBe('Summaries only');
      const summaries = text(root.querySelector('#mcp-level-aggregates-description'));
      expect(summaries).toContain('the form, the tables and their columns, column profiles such as counts and ranges, the scripts');
      expect(summaries).toContain('what the indicators and widgets show');
      expect(summaries).toContain('It never sees the rows of the tables.');
      expect(text(root.querySelector('#mcp-level-rows-title'))).toBe('Summaries and rows');
      const rows = text(root.querySelector('#mcp-level-rows-description'));
      expect(rows).toContain('Also the rows of the tables, the previews of script tests, and the values that scripts show with ctx.show.');
    });

    it('groups the options under a legend, each named by its title and described by its sentence', async () => {
      const { root } = await open('admin', { project: { mcpMaxAccess: 'rows' }, calls: [] });

      const fieldset = root.querySelector('[data-testid="mcp-form"] fieldset')!;
      expect(text(fieldset.querySelector('legend'))).toBe('Access for AI clients');
      expect(radios(root).map((input) => text(root.querySelector(`#${input.getAttribute('aria-labelledby')}`)))).toEqual([
        'Off',
        'Summaries only',
        'Summaries and rows',
      ]);
      for (const input of radios(root)) {
        const description = root.querySelector(`#${input.getAttribute('aria-describedby')}`);
        expect(text(description)).not.toBe('');
        expect(fieldset.contains(description)).toBe(true);
      }
    });

    it('starts with Off, and Save waits for a change', async () => {
      const { root } = await open('admin', { calls: [] });

      expect(radios(root).map((r) => r.checked)).toEqual([true, false, false]);
      expect(save(root).disabled).toBe(true);
    });

    it('saves the chosen level alone, shows the answer and says so (Admin allows summaries)', async () => {
      const opened = await open('admin', { calls: [] });
      const { root, http, harness } = opened;

      await choose(opened, 'aggregates');
      expect(save(root).disabled).toBe(false);
      byTestId<HTMLFormElement>(root, 'mcp-form')!.dispatchEvent(new Event('submit'));
      await settle(harness);
      const request = http.expectOne(projectUrl);

      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ mcpAccess: 'aggregates' });
      request.flush(details('admin', { mcpAccess: 'aggregates' }));
      await settle(harness);

      expect(radios(root).map((r) => r.checked)).toEqual([false, true, false]);
      expect(save(root).disabled).toBe(true);
      expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('Access for AI clients saved.');
    });

    it('can turn it off again', async () => {
      const opened = await open('admin', { project: { mcpAccess: 'aggregates' }, calls: [] });

      await choose(opened, 'off');
      save(opened.root).click();
      await settle(opened.harness);
      const request = opened.http.expectOne(projectUrl);

      expect(request.request.body).toEqual({ mcpAccess: 'off' });
      request.flush(details('admin', { mcpAccess: 'off' }));
      await settle(opened.harness);
      expect(radios(opened.root).map((r) => r.checked)).toEqual([true, false, false]);
    });

    it('offers rows when the deployment allows them, and saves it', async () => {
      const opened = await open('admin', { project: { mcpMaxAccess: 'rows' }, calls: [] });

      expect(radio(opened.root, 'rows').disabled).toBe(false);
      expect(byTestId(opened.root, 'mcp-rows-blocked')).toBeNull();
      await choose(opened, 'rows');
      save(opened.root).click();
      await settle(opened.harness);
      const request = opened.http.expectOne(projectUrl);

      expect(request.request.body).toEqual({ mcpAccess: 'rows' });
      request.flush(details('admin', { mcpAccess: 'rows', mcpMaxAccess: 'rows' }));
      await settle(opened.harness);
      expect(radios(opened.root).map((r) => r.checked)).toEqual([false, false, true]);
      expect(byTestId(opened.root, 'mcp-limited')).toBeNull();
    });

    it('disables rows, with the reason, while the deployment allows summaries only', async () => {
      const opened = await open('admin', { project: { mcpMaxAccess: 'aggregates' }, calls: [] });
      const { root } = opened;

      expect(radio(root, 'rows').disabled).toBe(true);
      expect(radio(root, 'off').disabled).toBe(false);
      expect(radio(root, 'aggregates').disabled).toBe(false);
      expect(text(byTestId(root, 'mcp-rows-blocked'))).toBe('The operator of this installation allows summaries only.');
      expect(byTestId(root, 'mcp-level-rows')!.contains(byTestId(root, 'mcp-rows-blocked'))).toBe(true);
      expect(byTestId(root, 'mcp-limited')).toBeNull();

      // A click on it changes nothing and cannot be saved.
      radio(root, 'rows').click();
      await settle(opened.harness);
      expect(radios(root).map((r) => r.checked)).toEqual([true, false, false]);
      expect(save(root).disabled).toBe(true);
    });

    it('treats a maximum the details do not give as summaries only', async () => {
      const { root } = await open('admin', { project: { mcpMaxAccess: null }, calls: [] });

      expect(radio(root, 'rows').disabled).toBe(true);
    });

    it('says the level is limited by the deployment when rows is stored but the maximum is summaries only', async () => {
      const opened = await open('admin', { project: { mcpAccess: 'rows', mcpMaxAccess: 'aggregates' }, calls: [] });
      const { root } = opened;

      expect(text(byTestId(root, 'mcp-limited'))).toBe(
        'This project is set to Summaries and rows, but the operator of this installation allows summaries only, so AI clients are limited to Summaries only.',
      );
      expect(radios(root).map((r) => r.checked)).toEqual([false, false, true]);
      expect(radio(root, 'rows').disabled).toBe(true);
      expect(save(root).disabled).toBe(true); // nothing to save: the API would refuse rows again

      await choose(opened, 'aggregates');
      expect(save(root).disabled).toBe(false);
      save(root).click();
      await settle(opened.harness);
      const request = opened.http.expectOne(projectUrl);
      expect(request.request.body).toEqual({ mcpAccess: 'aggregates' });
      request.flush(details('admin', { mcpAccess: 'aggregates', mcpMaxAccess: 'aggregates' }));
      await settle(opened.harness);
      expect(byTestId(root, 'mcp-limited')).toBeNull();
    });

    it("shows the API's message next to the options when it refuses the level, and keeps the stored one (Rows above the deployment's maximum)", async () => {
      const opened = await open('admin', { project: { mcpAccess: 'aggregates', mcpMaxAccess: 'rows' }, calls: [] });
      const { root, http, harness } = opened;

      await choose(opened, 'rows');
      save(root).click();
      await settle(harness);
      http
        .expectOne(projectUrl)
        .flush({ errors: { mcpAccess: ['The operator of this installation allows summaries only.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);

      const message = byTestId(root, 'mcp-error')!;
      expect(text(message)).toBe('The operator of this installation allows summaries only.');
      expect(message.getAttribute('lang')).toBe('en'); // the API wrote it
      expect(root.querySelector('[data-testid="mcp-form"] fieldset')?.getAttribute('aria-describedby')).toBe('mcpAccess-error');
      expect(TestBed.inject(ToastService).toasts()).toEqual([]);

      // The page's project still has the stored level: choosing it again leaves nothing to save.
      await choose(opened, 'aggregates');
      expect(save(root).disabled).toBe(true);
    });

    it('clears the message when the next try goes through', async () => {
      const opened = await open('admin', { project: { mcpAccess: 'off', mcpMaxAccess: 'rows' }, calls: [] });
      const { root, http, harness } = opened;
      await choose(opened, 'rows');
      save(root).click();
      await settle(harness);
      http.expectOne(projectUrl).flush({ errors: { mcpAccess: ['Not now.'] } }, { status: 400, statusText: 'Bad Request' });
      await settle(harness);
      expect(byTestId(root, 'mcp-error')).not.toBeNull();

      save(root).click();
      await settle(harness);
      expect(byTestId(root, 'mcp-error')).toBeNull();
      http.expectOne(projectUrl).flush(details('admin', { mcpAccess: 'rows', mcpMaxAccess: 'rows' }));
    });

    it('reports another failure in a toast, with the options as they were', async () => {
      const opened = await open('admin', { calls: [] });
      await choose(opened, 'aggregates');
      save(opened.root).click();
      await settle(opened.harness);

      opened.http.expectOne(projectUrl).flush({ title: 'Forbidden', status: 403 }, { status: 403, statusText: 'Forbidden' });
      await settle(opened.harness);

      expect(byTestId(opened.root, 'mcp-error')).toBeNull();
      expect(TestBed.inject(ToastService).toasts().at(-1)?.message).toBe('Forbidden');
      expect(radios(opened.root).map((r) => r.checked)).toEqual([false, true, false]);
      expect(save(opened.root).disabled).toBe(false);
    });

    it('does not send the same level twice while a save is under way', async () => {
      const opened = await open('admin', { calls: [] });
      await choose(opened, 'aggregates');

      save(opened.root).click();
      await settle(opened.harness);

      expect(save(opened.root).disabled).toBe(true);
      expect(radios(opened.root).every((r) => r.disabled)).toBe(true);
      opened.http.expectOne(projectUrl).flush(details('admin', { mcpAccess: 'aggregates' }));
    });
  });

  describe('for an analyst', () => {
    it.each<[McpLevel, string, string]>([
      ['off', 'Off', 'AI clients see nothing of this project.'],
      ['aggregates', 'Summaries only', 'It never sees the rows of the tables.'],
      ['rows', 'Summaries and rows', 'the previews of script tests'],
    ])('shows the level %s as a value, without options or a Save button', async (level, title, sentence) => {
      const { root } = await open('analyst', { project: { mcpAccess: level, mcpMaxAccess: 'rows' }, calls: [] });

      expect(text(byTestId(root, 'mcp-level'))).toBe(title);
      expect(text(byTestId(root, 'mcp-readonly'))).toContain(sentence);
      expect(text(byTestId(root, 'mcp-card'))).toContain('Only a project admin can change this.');
      expect(byTestId(root, 'mcp-form')).toBeNull();
      expect(root.querySelectorAll('[data-testid="mcp-card"] input')).toHaveLength(0);
      expect(byTestId(root, 'mcp-save')).toBeNull();
    });

    it('says the level is limited by the deployment when rows is stored but the maximum is summaries only', async () => {
      const { root } = await open('analyst', { project: { mcpAccess: 'rows', mcpMaxAccess: 'aggregates' }, calls: [] });

      expect(text(byTestId(root, 'mcp-level'))).toBe('Summaries and rows');
      expect(text(byTestId(root, 'mcp-limited'))).toContain('the operator of this installation allows summaries only');
      expect(text(byTestId(root, 'mcp-limited'))).toContain('limited to Summaries only');
    });

    it('does not say it when the level is within the maximum', async () => {
      const { root } = await open('analyst', { project: { mcpAccess: 'rows', mcpMaxAccess: 'rows' }, calls: [] });

      expect(byTestId(root, 'mcp-limited')).toBeNull();
    });
  });

  describe('recent activity', () => {
    it.each<ProjectRole>(['analyst', 'admin'])('is read for an %s when the card is shown, once', async (role) => {
      const opened = await open(role);
      const request = opened.http.expectOne(callsUrl);

      expect(request.request.method).toBe('GET');
      request.flush({ items: [] });
      await settle(opened.harness);
      opened.http.expectNone(callsUrl);
    });

    it('is not requested for a viewer, who never reaches the section (Viewer)', async () => {
      const opened = await openProjectSection('overview', 'viewer');

      expect(byTestId(opened.root, 'mcp-card')).toBeNull();
      opened.http.expectNone(callsUrl);
    });

    it('shows a placeholder while it loads', async () => {
      const { root, http } = await open('admin');

      expect(byTestId(root, 'mcp-activity-loading')?.getAttribute('aria-busy')).toBe('true');
      expect(text(root.querySelector('#mcp-activity-title'))).toBe('Recent activity');
      http.expectOne(callsUrl).flush({ items: [] });
    });

    it('lists the calls newest first, each with its time, person, tool and outcome (Analyst checks what Claude did)', async () => {
      const calls = [
        call('c4', { startedAt: '2026-10-28T15:04:00Z', tool: 'create_widget' }),
        call('c3', { startedAt: '2026-10-28T15:03:00Z', tool: 'create_widget' }),
        call('c2', { startedAt: '2026-10-28T15:02:00Z', tool: 'create_dashboard', userName: 'Grace Hopper' }),
        call('c1', { startedAt: '2026-10-28T15:01:00Z', tool: 'query_rows', status: 'refused', errorCode: 'mcp.rowsNotAllowed', access: null }),
      ];
      const { root } = await open('analyst', { calls });

      expect(byTestId(root, 'mcp-activity-empty')).toBeNull();
      const rows = [...root.querySelectorAll('[data-testid="mcp-call"]')];
      expect(rows.map((row) => text(byTestId(row as HTMLElement, 'mcp-call-tool')))).toEqual(['create_widget', 'create_widget', 'create_dashboard', 'query_rows']);
      expect(rows.map((row) => text(byTestId(row as HTMLElement, 'mcp-call-person')))).toEqual(['Ada Lovelace', 'Ada Lovelace', 'Grace Hopper', 'Ada Lovelace']);
      expect(rows.map((row) => text(byTestId(row as HTMLElement, 'mcp-call-status')))).toEqual(['Succeeded', 'Succeeded', 'Succeeded', 'Refused']);
      expect(text(byTestId(rows[0] as HTMLElement, 'mcp-call-when'))).toContain('Oct 28, 2026');
      const headers = [...root.querySelectorAll('[data-testid="mcp-calls"] th')].map(text);
      expect(headers).toEqual(['When', 'Person', 'Tool', 'Outcome']);
      expect(root.querySelector('[data-testid="mcp-calls"]')?.getAttribute('aria-label')).toBe('Recent calls from AI clients');
    });

    it('shows the level a call ran at and the code of a refusal or failure', async () => {
      const calls = [
        call('c3', { tool: 'query_rows', status: 'refused', errorCode: 'mcp.rowsNotAllowed', access: 'aggregates' }),
        call('c2', { tool: 'save_script', status: 'failed', errorCode: 'mcp.scriptChanged', access: 'rows' }),
        call('c1', { tool: 'list_projects', status: 'succeeded', errorCode: null, access: null }),
      ];
      const { root } = await open('admin', { calls });
      const rows = [...root.querySelectorAll<HTMLElement>('[data-testid="mcp-call"]')];

      expect(text(byTestId(rows[0], 'mcp-call-status'))).toBe('Refused');
      expect(text(byTestId(rows[0], 'mcp-call-code'))).toBe('mcp.rowsNotAllowed');
      expect(text(byTestId(rows[0], 'mcp-call-access'))).toBe('Summaries only');
      expect(text(byTestId(rows[1], 'mcp-call-status'))).toBe('Failed');
      expect(text(byTestId(rows[1], 'mcp-call-code'))).toBe('mcp.scriptChanged');
      expect(text(byTestId(rows[1], 'mcp-call-access'))).toBe('Summaries and rows');
      expect(byTestId(rows[2], 'mcp-call-code')).toBeNull();
      expect(byTestId(rows[2], 'mcp-call-access')).toBeNull();
    });

    it("writes the time in the project's time zone, and says which", async () => {
      const { root } = await open('analyst', { project: { timeZone: 'America/Bogota' }, calls: [call('c1', { startedAt: '2026-10-28T15:04:00Z' })] });

      expect(text(byTestId(root, 'mcp-call-when'))).toMatch(/^Oct 28, 2026, 10:04 AM$/);
      expect(text(byTestId(root, 'mcp-activity'))).toContain("Times are in the project's time zone (America/Bogota).");
    });

    it('does not move a time that falls on another day in the project zone to the browser day', async () => {
      const { root } = await open('analyst', { project: { timeZone: 'Pacific/Auckland' }, calls: [call('c1', { startedAt: '2026-10-28T15:04:00Z' })] });

      expect(text(byTestId(root, 'mcp-call-when'))).toMatch(/^Oct 29, 2026, 4:04 AM$/);
    });

    it('falls back to the browser zone for a zone it does not know, instead of failing', async () => {
      const { root } = await open('analyst', { project: { timeZone: 'Mars/Olympus' }, calls: [call('c1')] });

      expect(text(byTestId(root, 'mcp-call-when'))).toContain('Oct 28, 2026');
    });

    it('says no AI client has used the project yet when the list is empty', async () => {
      const { root } = await open('admin', { calls: [] });

      expect(text(byTestId(root, 'mcp-activity-empty'))).toBe('No AI client has used this project yet.');
      expect(byTestId(root, 'mcp-calls')).toBeNull();
    });

    it('says it could not load, with a button to try again, and shows the list when the second try works', async () => {
      const { root, http, harness } = await open('analyst');
      http.expectOne(callsUrl).flush(null, { status: 500, statusText: 'Server Error' });
      await settle(harness);

      expect(text(byTestId(root, 'mcp-activity-error'))).toContain('The recent activity could not be loaded.');
      expect(byTestId(root, 'mcp-activity-loading')).toBeNull();
      expect(byTestId(root, 'mcp-form') ?? byTestId(root, 'mcp-readonly')).not.toBeNull(); // the level is still there

      byTestId<HTMLButtonElement>(root, 'mcp-activity-retry')!.click();
      await settle(harness);
      http.expectOne(callsUrl).flush({ items: [call('c1')] });
      await settle(harness);

      expect(byTestId(root, 'mcp-activity-error')).toBeNull();
      expect(root.querySelectorAll('[data-testid="mcp-call"]')).toHaveLength(1);
    });

    it('does not read the list again after a save', async () => {
      const opened = await open('admin', { calls: [] });
      await choose(opened, 'aggregates');
      save(opened.root).click();
      await settle(opened.harness);
      opened.http.expectOne(projectUrl).flush(details('admin', { mcpAccess: 'aggregates' }));
      await settle(opened.harness);

      opened.http.expectNone(callsUrl);
    });
  });

  describe('on a narrow screen', () => {
    // jsdom has no layout: these check the classes that keep the card inside a 390 px screen. The stack verification looks at it in Chrome.
    const long = call('c1', {
      userName: 'A person with a very long name '.repeat(4),
      tool: 'restore_script_version',
      errorCode: 'mcp.' + 'x'.repeat(60),
      status: 'failed',
      access: 'rows',
    });

    it('lists the calls as cards below the md breakpoint and as a table from it', async () => {
      const { root } = await open('admin', { calls: [long, call('c2', { tool: 'query_rows', status: 'refused', errorCode: 'mcp.projectOff', access: null })] });

      const cards = root.querySelector('[data-testid="mcp-call-cards"]')!;
      expect(cards.className).toContain('md:hidden');
      const container = root.querySelector('[data-testid="mcp-activity"] tmz-table-container')!;
      expect(container.className).toContain('hidden');
      expect(container.className).toContain('md:block');
      expect(container.className).toContain('overflow-x-auto');
      expect(cards.querySelectorAll('[data-testid="mcp-call-card"]')).toHaveLength(2);
      expect(root.querySelectorAll('[data-testid="mcp-call"]')).toHaveLength(2);
    });

    it('gives a card the facts of a row: outcome, tool, time, person, level and code', async () => {
      const { root } = await open('analyst', { project: { timeZone: 'America/Bogota' }, calls: [long] });

      const card = text(root.querySelector('[data-testid="mcp-call-card"]'));
      expect(card).toContain('Failed');
      expect(card).toContain('restore_script_version');
      expect(card).toContain('Oct 28, 2026, 10:04 AM');
      expect(card).toContain('A person with a very long name');
      expect(card).toContain('Summaries and rows');
      expect(card).toContain('mcp.xxxxxxxx');
    });

    it('keeps the card inside the page: long names wrap, the card may shrink in its grid and takes the whole row on wide screens', async () => {
      const { root } = await open('admin', { calls: [long] });

      const card = root.querySelector('[data-testid="mcp-call-card"]')!;
      expect(card.querySelector('code')?.className).toContain('break-all');
      expect(card.querySelector('p')?.className).toContain('break-words');
      expect(byTestId(root, 'mcp-call-person')?.className).toContain('break-words');
      expect(byTestId(root, 'mcp-call-tool')?.className).toContain('break-all');
      expect(byTestId(root, 'mcp-call-code')?.className).toContain('break-all');
      // A grid item whose content is wider than the screen would stretch the whole column unless it may shrink.
      expect(root.querySelector('app-project-mcp-access')?.className).toContain('min-w-0');
      expect(root.querySelector('app-project-mcp-access')?.className).toContain('lg:col-span-2');
      for (const label of root.querySelectorAll('[data-testid="mcp-form"] label')) {
        expect(label.className).not.toContain('whitespace-nowrap');
      }
      expect(root.querySelector('[data-testid="mcp-card"]')?.innerHTML).not.toMatch(/min-w-\[|w-\[\d+px\]/);
    });
  });

  describe('in Spanish', () => {
    it('reads its text from es.json: the title, the options, the activity and the outcomes', async () => {
      const { root } = await open('admin', {
        project: { mcpAccess: 'aggregates', mcpMaxAccess: 'aggregates' },
        calls: [call('c1', { status: 'refused', errorCode: 'mcp.projectOff', access: 'aggregates' })],
        lang: 'es',
      });

      expect(text(root.querySelector('[data-testid="mcp-card"] h2'))).toBe(es.settings.mcp.title);
      expect(text(root.querySelector('[data-testid="mcp-form"] legend'))).toBe(es.settings.mcp.access);
      expect(text(root.querySelector('#mcp-level-rows-title'))).toBe(es.settings.mcp.levels.rows.title);
      expect(text(byTestId(root, 'mcp-rows-blocked'))).toBe(es.settings.mcp.rowsBlocked);
      expect(text(root.querySelector('#mcp-activity-title'))).toBe(es.settings.mcp.activityTitle);
      expect(text(byTestId(root, 'mcp-call-status'))).toBe(es.settings.mcp.status.refused);
      expect(text(byTestId(root, 'mcp-call-access'))).toBe(es.settings.mcp.levels.aggregates.title);
      expect([...root.querySelectorAll('[data-testid="mcp-calls"] th')].map(text)).toEqual([
        es.settings.mcp.columns.when,
        es.settings.mcp.columns.person,
        es.settings.mcp.columns.tool,
        es.settings.mcp.columns.outcome,
      ]);
    });
  });
});

type McpLevel = 'off' | 'aggregates' | 'rows';
