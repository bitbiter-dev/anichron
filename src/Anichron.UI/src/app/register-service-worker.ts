import { listen } from '../shared/listen';
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
  apply: () => void;
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
  listen(document, 'visibilitychange', () => {
    if (document.visibilityState === 'visible') check();
  });
}

export function registerServiceWorker(production: boolean, reload: () => void): Updates {
  const changes = new EventTarget();
  let registration: ServiceWorkerRegistration | undefined;
  let offered = false;
  const offer = () => {
    if (!registration?.waiting || !registration.active) return;
    offered = true;
    changes.dispatchEvent(new Event('change'));
  };
  const updates: Updates = {
    waiting: () => offered,
    subscribe: (listener) => listen(changes, 'change', listener),
    apply: () => {
      const worker = registration?.waiting;
      if (!worker) {
        if (offered) reload();
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
      registered.addEventListener('updatefound', () => {
        registered.installing?.addEventListener('statechange', offer);
      });
      offer();
      checkHourly(registered);
    })
    .catch(() => undefined);
  return updates;
}
