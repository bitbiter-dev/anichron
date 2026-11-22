import { expect, it } from 'vitest';
import { unreachableProblem } from '../api/problem';
import { createStartRetry } from './start-retry';
import { advance } from '../test/time';

it('tells its subscriber when a retry starts and when it fails', async () => {
  const retry = createStartRetry(
    () => Promise.reject(new Error('Failed to fetch')),
    unreachableProblem(),
    () => undefined,
  );
  const pending: boolean[] = [];
  const stop = retry.subscribe(() => {
    pending.push(retry.state().pending);
  });

  await advance(5000);
  stop();

  expect(pending).toEqual([true, false]);
});
