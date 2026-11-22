import { createChanges, listen, onVisible } from '../shared/listen';
import { takeOver } from './take-over';

interface TrustedTypesFactory {
  createPolicy(
    name: string,
    rules: { createScriptURL(url: string): string },
  ): { createScriptURL(url: string): string };
}

export interface Updates {
  waiting: () => boolean;
  subscribe: (listener: () => void) => () => void;
  reload: () => void;
}

const checkMilliseconds = 60 * 60 * 1000;

function scriptUrl() {
  const url = '/sw.js';
  const { trustedTypes } = globalThis as { trustedTypes?: TrustedTypesFactory };
  return (
    trustedTypes
      ?.createPolicy('service-worker', { createScriptURL: () => url })
      .createScriptURL(url) ?? url
  );
}

function checkHourly(registration: ServiceWorkerRegistration) {
  let checked = -Infinity;
  const check = () => {
    if (Date.now() - checked < checkMilliseconds) return;
    checked = Date.now();
    registration.update().catch(() => undefined);
  };
  check();
  onVisible(check);
}

export function registerServiceWorker(production: boolean, reload: () => void): Updates {
  const changes = createChanges();
  let registration: ServiceWorkerRegistration | undefined;
  let offered = false;
  const updates: Updates = {
    waiting: () => offered,
    subscribe: changes.subscribe,
    reload: () => {
      const worker = registration?.waiting;
      if (!worker || !navigator.serviceWorker.controller) {
        reload();
        return;
      }
      listen(navigator.serviceWorker, 'controllerchange', reload);
      worker.postMessage(takeOver());
    },
  };
  if (!production || !('serviceWorker' in navigator)) return updates;
  navigator.serviceWorker
    .register(scriptUrl())
    .then((registered) => {
      registration = registered;
      const offer = () => {
        if (!registered.waiting || !navigator.serviceWorker.controller) return;
        offered = true;
        changes.notify();
      };
      const watchInstalling = () => {
        registered.installing?.addEventListener('statechange', offer);
      };
      registered.addEventListener('updatefound', watchInstalling);
      watchInstalling();
      offer();
      checkHourly(registered);
    })
    .catch(() => undefined);
  return updates;
}
