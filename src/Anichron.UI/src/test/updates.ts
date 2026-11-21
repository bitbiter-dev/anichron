import { act } from 'react';
import { vi } from 'vitest';
import type { Updates } from '../app/register-service-worker';
import { listen } from '../shared/listen';

export function fakeUpdates() {
  const changes = new EventTarget();
  let waiting = false;
  const updates = {
    waiting: () => waiting,
    subscribe: (listener: () => void) => listen(changes, 'change', listener),
    reload: vi.fn(),
  } satisfies Updates;
  return {
    ...updates,
    arrive() {
      act(() => {
        waiting = true;
        changes.dispatchEvent(new Event('change'));
      });
    },
  };
}
