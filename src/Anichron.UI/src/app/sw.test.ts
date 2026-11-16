import { beforeEach, expect, it, vi } from 'vitest';

const addEventListener = vi.fn();

beforeEach(() => {
  vi.stubGlobal('self', {
    addEventListener,
    __WB_MANIFEST: [{ url: 'index.html', revision: 'r1' }],
  });
  return () => vi.unstubAllGlobals();
});

it('serves the app shell from the worker scope', async () => {
  await import(`./sw?run=${crypto.randomUUID()}`);

  expect(addEventListener.mock.calls.map(([type]) => type as string)).toEqual([
    'install',
    'activate',
    'fetch',
  ]);
});
