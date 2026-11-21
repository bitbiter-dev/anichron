import { expect, it, vi } from 'vitest';
import { unreachableProblem } from '../api/problem';
import { createStartRetry } from './start-retry';

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

  await vi.advanceTimersByTimeAsync(5000);
  stop();

  expect(pending).toEqual([true, false]);
});
