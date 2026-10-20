import en from '../../../../public/i18n/en.json';

/** Codes the API returns in Problem Details or records on failed synchronizations, runs and tests. Each needs a message. */
const apiErrorCodes = [
  'kobo.invalid_url',
  'kobo.https_required',
  'kobo.address_not_allowed',
  'kobo.unauthorized',
  'kobo.form_not_found',
  'kobo.unreachable',
  'kobo.unexpected_response',
  'kobo.token_unreadable',
  'kobo.version_not_found',
  'kobo.rate_limited',
  'member.exists',
  'invitation.exists',
  'project.last_admin',
  'project.create_not_allowed',
  'sync.in_progress',
  'sync.interrupted',
  'sync.failed',
  'webhook.unsupported_media_type',
  'webhook.invalid_submission',
  'webhook.too_large',
  'run.already_queued',
  'run.interrupted',
  'limit.time',
  'limit.memory',
  'step.flatten',
  'step.clean',
  'step.output',
  'step.indicator',
  'script.not_set',
  'test.interrupted',
  'reference.exists',
  'reference.limit',
  'dashboard.timeout',
  'dashboards.notFound',
  'dashboards.limit',
  'dashboards.lastOne',
  'dashboards.orderChanged',
  'data.timeout',
  'data.tableNotFound',
  'data.columnNotFound',
  'data.exportBusy',
  'data.xlsxTooManyRows',
  'data.exportTooLarge',
  'indicators.notPublished',
  'samplingFrame.none',
  'assistant.disabled',
  'assistant.misconfigured',
  'assistant.unavailable',
  'assistant.timeout',
  'assistant.rateLimited',
  'assistant.budget',
  'assistant.busy',
  'assistant.testStale',
  'assistant.contextTooLarge',
  'basemaps.unavailable',
  'basemaps.upstream',
  'basemaps.rateLimited',
  'accountEmail.disabled',
  'accountEmail.existingAccount',
  'accountEmail.noAccount',
  'accountEmail.rateLimited',
  'accountEmail.failed',
];

function lookup(root: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown> | undefined)?.[key], root);
}

describe('English translations', () => {
  it.each(apiErrorCodes)('have a message for the API error code %s', (code) => {
    expect(typeof lookup(en.errors.codes, code)).toBe('string');
  });

  it('have the assistant messages that name a time, for the codes that carry one', () => {
    expect(en.errors.codes.assistant.rateLimitedIn).toContain('{{ seconds }}');
    expect(en.errors.codes.assistant.budgetRenews).toContain('{{ when }}');
  });

  it('have the account email message that names the minutes to wait, in both forms', () => {
    expect(en.errors.codes.accountEmail.rateLimitedIn).toContain('{{ minutes }}');
    expect(en.errors.codes.accountEmail.rateLimitedInOne).not.toContain('{{');
  });

  it('name every run trigger, status, step and step status', () => {
    for (const key of ['manual', 'sync', 'schedule']) expect(typeof lookup(en.runs.triggers, key)).toBe('string');
    for (const key of ['queued', 'running', 'succeeded', 'failed']) expect(typeof lookup(en.runs.status, key)).toBe('string');
    for (const key of ['flatten', 'clean', 'output', 'indicator']) expect(typeof lookup(en.runs.steps, key)).toBe('string');
    for (const key of ['succeeded', 'failed', 'skipped']) expect(typeof lookup(en.runs.stepStatus, key)).toBe('string');
  });

  it('name every column type of previews and reference tables', () => {
    for (const key of ['integer', 'decimal', 'boolean', 'date', 'timestamp', 'text']) expect(typeof lookup(en.preview.types, key)).toBe('string');
  });

  it('name every synchronization kind, trigger and status', () => {
    for (const key of ['incremental', 'full', 'reconciliation']) expect(typeof lookup(en.data.kind, key)).toBe('string');
    for (const key of ['schedule', 'manual', 'initial']) expect(typeof lookup(en.data.trigger, key)).toBe('string');
    for (const key of ['queued', 'running', 'succeeded', 'failed']) expect(typeof lookup(en.data.status, key)).toBe('string');
  });
});
