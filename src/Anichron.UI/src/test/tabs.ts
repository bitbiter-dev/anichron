import type { TabMessage, Tabs } from '../api/tabs';

interface Listener {
  tab: object;
  handler: (message: TabMessage) => void;
}

const settle = () => undefined;

export function sharedTabs() {
  let lock: Promise<unknown> = Promise.resolve();
  const listeners = new Set<Listener>();
  return {
    tab(): Tabs {
      const tab = {};
      return {
        exclusively(task) {
          const run = lock.then(task);
          lock = run.then(settle, settle);
          return run;
        },
        announce(message) {
          for (const listener of listeners)
            if (listener.tab !== tab)
              queueMicrotask(() => {
                listener.handler(message);
              });
        },
        listen(handler) {
          const listener = { tab, handler };
          listeners.add(listener);
          return () => listeners.delete(listener);
        },
      };
    },
  };
}
