import { vi } from 'vitest';
import { axe } from 'vitest-axe';

// axe yields through setTimeout, which the fake clock holds until it is advanced.
export async function axeResults(container: Element) {
  const finished = { done: false };
  const results = axe(container).finally(() => {
    finished.done = true;
  });
  for (let step = 0; !finished.done; step++) {
    if (step === 10_000) throw new Error('axe did not finish within 10 s of fake time.');
    await vi.advanceTimersByTimeAsync(1);
  }
  return results;
}
