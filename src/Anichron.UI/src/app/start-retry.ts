import { problemOf, type Problem } from '../api/problem';
import { createBackoff } from '../shared/backoff';
import { createChanges, isVisible, listen, onVisibilityChange } from '../shared/listen';

export function createStartRetry(
  restore: () => Promise<void>,
  failure: Problem,
  countdown: (seconds: number) => void,
) {
  let state = { problem: failure, pending: false };
  const backoff = createBackoff({ firstSeconds: 5, longestSeconds: 60 });
  const changes = createChanges();
  let subscribed = false;
  let notBefore = 0;
  let wait = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;

  function after({ retryAfterSeconds }: Problem) {
    notBefore = Date.now() + retryAfterSeconds * 1000;
    wait = backoff.waitMilliseconds(retryAfterSeconds);
  }

  function schedule(milliseconds: number) {
    clearTimeout(timer);
    if (!isVisible()) return;
    timer = setTimeout(attempt, Math.max(milliseconds, notBefore - Date.now()));
  }

  function set(next: Partial<typeof state>) {
    state = { ...state, ...next };
    changes.notify();
  }

  function attempt() {
    if (state.pending) return;
    set({ pending: true });
    restore().catch((error: unknown) => {
      if (!subscribed) return;
      const problem = problemOf(error);
      set({ problem, pending: false });
      countdown(problem.retryAfterSeconds);
      after(problem);
      schedule(wait);
    });
  }

  after(failure);

  return {
    state: () => state,
    subscribe: (listener: () => void) => {
      subscribed = true;
      const stopListening = [
        changes.subscribe(listener),
        listen(window, 'online', () => {
          schedule(0);
        }),
        onVisibilityChange(() => {
          backoff.reset();
          schedule(0);
        }),
      ];
      schedule(wait);
      return () => {
        subscribed = false;
        clearTimeout(timer);
        for (const stop of stopListening) stop();
      };
    },
  };
}
