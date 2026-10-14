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
  'step.indicators',
  'script.not_set',
  'test.interrupted',
  'reference.exists',
  'reference.limit',
];

function lookup(root: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown> | undefined)?.[key], root);
}

describe('English translations', () => {
  it.each(apiErrorCodes)('have a message for the API error code %s', (code) => {
    expect(typeof lookup(en.errors.codes, code)).toBe('string');
  });

  it('name every run trigger, status, step and step status', () => {
    for (const key of ['manual', 'sync', 'schedule']) expect(typeof lookup(en.runs.triggers, key)).toBe('string');
    for (const key of ['queued', 'running', 'succeeded', 'failed']) expect(typeof lookup(en.runs.status, key)).toBe('string');
    for (const key of ['flatten', 'clean', 'output', 'indicators']) expect(typeof lookup(en.runs.steps, key)).toBe('string');
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
