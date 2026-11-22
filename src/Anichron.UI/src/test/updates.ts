import { act } from 'react';
import { vi } from 'vitest';
import type { Updates } from '../app/register-service-worker';
import { createChanges } from '../shared/listen';

export function fakeUpdates() {
  const changes = createChanges();
  let waiting = false;
  const updates = {
    waiting: () => waiting,
    subscribe: changes.subscribe,
    reload: vi.fn(),
  } satisfies Updates;
  return {
    ...updates,
    arrive() {
      act(() => {
        waiting = true;
        changes.notify();
      });
    },
  };
}
