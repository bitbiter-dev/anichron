import { vi } from 'vitest';
import { axe } from 'vitest-axe';

// axe yields through setTimeout, which the fake clock holds until it is advanced.
export async function axeResults(container: Element) {
  const results = axe(container);
  await vi.runAllTimersAsync();
  return results;
}
