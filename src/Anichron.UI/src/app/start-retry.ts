import { problemOf, type Problem } from '../api/problem';
import { listen } from '../shared/listen';

const firstWaitSeconds = 5;
const longestWaitSeconds = 60;

function waitSeconds(step: number) {
  return Math.min(firstWaitSeconds * 2 ** step, longestWaitSeconds);
}

function idle() {
  return undefined;
}

function isVisible() {
  return document.visibilityState === 'visible';
}

export function createStartRetry(
  restore: () => Promise<void>,
  failure: Problem,
  countdown: (seconds: number) => void,
) {
  let state = { problem: failure, pending: false };
  let step = 0;
  let notBefore = 0;
  let wait = 0;
  let changed: () => void = idle;
  let timer: ReturnType<typeof setTimeout> | undefined;

  function after({ retryAfterSeconds }: Problem) {
    notBefore = Date.now() + retryAfterSeconds * 1000;
    wait = retryAfterSeconds ? 0 : waitSeconds(step++) * 1000;
  }

  function schedule(milliseconds: number) {
    clearTimeout(timer);
    if (!isVisible()) return;
    timer = setTimeout(attempt, Math.max(milliseconds, notBefore - Date.now()));
  }

  function set(next: Partial<typeof state>) {
    state = { ...state, ...next };
    changed();
  }

  function attempt() {
    if (state.pending) return;
    set({ pending: true });
    restore().catch((error: unknown) => {
      if (changed === idle) return;
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
      changed = listener;
      const stopListening = [
        listen(window, 'online', () => {
          schedule(0);
        }),
        listen(document, 'visibilitychange', () => {
          step = 0;
          schedule(0);
        }),
      ];
      schedule(wait);
      return () => {
        changed = idle;
        clearTimeout(timer);
        for (const stop of stopListening) stop();
      };
    },
  };
}
