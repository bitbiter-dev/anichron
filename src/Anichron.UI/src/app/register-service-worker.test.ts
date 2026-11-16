import { beforeEach, expect, it, vi } from 'vitest';
import { registerServiceWorker } from './register-service-worker';

const register = vi.fn<(url: unknown) => Promise<unknown>>();

function trustedScriptUrls() {
  const policies: string[] = [];
  vi.stubGlobal('trustedTypes', {
    createPolicy(name: string, rules: { createScriptURL(url: string): string }) {
      policies.push(name);
      return { createScriptURL: (url: string) => ({ trusted: rules.createScriptURL(url) }) };
    },
  });
  return policies;
}

beforeEach(() => {
  register.mockResolvedValue({});
  Object.defineProperty(navigator, 'serviceWorker', { value: { register }, configurable: true });
  return () => {
    Reflect.deleteProperty(navigator, 'serviceWorker');
    vi.unstubAllGlobals();
  };
});

it('registers the service worker in a production build', () => {
  registerServiceWorker(true);

  expect(register).toHaveBeenCalledExactlyOnceWith('/sw.js');
});

it('registers none under the dev server', () => {
  registerServiceWorker(false);

  expect(register).not.toHaveBeenCalled();
});

it('passes the script URL through a Trusted Types policy where the browser enforces them', () => {
  const policies = trustedScriptUrls();

  registerServiceWorker(true);

  expect(policies).toEqual(['service-worker']);
  expect(register).toHaveBeenCalledExactlyOnceWith({ trusted: '/sw.js' });
});

it('starts the app as before in a browser without service workers', () => {
  Reflect.deleteProperty(navigator, 'serviceWorker');

  expect(() => {
    registerServiceWorker(true);
  }).not.toThrow();
});
