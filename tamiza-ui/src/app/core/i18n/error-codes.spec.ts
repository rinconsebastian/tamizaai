import en from '../../../../public/i18n/en.json';

/** Codes the API returns in Problem Details or records on failed synchronizations. Each needs a message. */
const apiErrorCodes = [
  'kobo.invalid_url',
  'kobo.https_required',
  'kobo.address_not_allowed',
  'kobo.unauthorized',
  'kobo.form_not_found',
  'kobo.unreachable',
  'kobo.unexpected_response',
  'kobo.token_unreadable',
  'member.exists',
  'invitation.exists',
  'project.last_admin',
  'sync.in_progress',
  'sync.interrupted',
  'sync.failed',
  'webhook.unsupported_media_type',
  'webhook.invalid_submission',
  'webhook.too_large',
];

function lookup(root: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>((node, key) => (node as Record<string, unknown> | undefined)?.[key], root);
}

describe('English translations', () => {
  it.each(apiErrorCodes)('have a message for the API error code %s', (code) => {
    expect(typeof lookup(en.errors.codes, code)).toBe('string');
  });

  it('name every synchronization kind, trigger and status', () => {
    for (const key of ['incremental', 'full', 'reconciliation']) expect(typeof lookup(en.data.kind, key)).toBe('string');
    for (const key of ['schedule', 'manual', 'initial']) expect(typeof lookup(en.data.trigger, key)).toBe('string');
    for (const key of ['queued', 'running', 'succeeded', 'failed']) expect(typeof lookup(en.data.status, key)).toBe('string');
  });
});
