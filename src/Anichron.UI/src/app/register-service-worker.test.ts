import { beforeEach, expect, it, vi } from 'vitest';
import { becomes } from '../test/visibility';
import { registerServiceWorker } from './register-service-worker';

const hour = 60 * 60 * 1000;

class FakeWorker extends EventTarget {
  state = 'installing';
  readonly postMessage = vi.fn();

  becomes(state: string) {
    this.state = state;
    this.dispatchEvent(new Event('statechange'));
  }
}

class FakeRegistration extends EventTarget {
  installing: FakeWorker | null = null;
  waiting: FakeWorker | null = null;
  active: FakeWorker | null = new FakeWorker();
  readonly update = vi.fn(() => Promise.resolve());

  found() {
    const worker = new FakeWorker();
    this.installing = worker;
    this.dispatchEvent(new Event('updatefound'));
    return worker;
  }

  installed(worker: FakeWorker) {
    this.installing = null;
    this.waiting = worker;
    worker.becomes('installed');
  }

  activated(worker: FakeWorker) {
    this.waiting = null;
    this.active = worker;
    worker.becomes('activated');
  }
}

class FakeContainer extends EventTarget {
  controller: FakeWorker | null;
  readonly registration: FakeRegistration;
  readonly register = vi.fn<(url: unknown) => Promise<FakeRegistration>>();

  constructor(registration: FakeRegistration) {
    super();
    this.registration = registration;
    this.controller = registration.active;
    this.register.mockResolvedValue(registration);
  }

  takenOverBy(worker: FakeWorker) {
    this.registration.activated(worker);
    this.controller = worker;
    this.dispatchEvent(new Event('controllerchange'));
  }
}

let container: FakeContainer;
let registration: FakeRegistration;
const reload = vi.fn();

function serveWith(serviceWorker: FakeContainer) {
  container = serviceWorker;
  registration = serviceWorker.registration;
  Object.defineProperty(navigator, 'serviceWorker', { value: serviceWorker, configurable: true });
}

async function registered(production = true) {
  const updates = registerServiceWorker(production, reload);
  await vi.advanceTimersByTimeAsync(0);
  return updates;
}

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
  reload.mockReset();
  serveWith(new FakeContainer(new FakeRegistration()));
  return () => {
    Reflect.deleteProperty(navigator, 'serviceWorker');
    vi.unstubAllGlobals();
  };
});

it('registers the service worker in a production build', async () => {
  await registered();

  expect(container.register).toHaveBeenCalledExactlyOnceWith('/sw.js');
});

it('registers none under the dev server, and never offers a version', async () => {
  const updates = await registered(false);

  expect(container.register).not.toHaveBeenCalled();
  expect(updates.waiting()).toBe(false);
});

it('passes the script URL through a Trusted Types policy where the browser enforces them', async () => {
  const policies = trustedScriptUrls();

  await registered();

  expect(policies).toEqual(['service-worker']);
  expect(container.register).toHaveBeenCalledExactlyOnceWith({ trusted: '/sw.js' });
});

it('starts the app as before in a browser without service workers', async () => {
  Reflect.deleteProperty(navigator, 'serviceWorker');

  const updates = await registered();

  expect(updates.waiting()).toBe(false);
  updates.reload();
  expect(reload).toHaveBeenCalledOnce();
});

it('starts the app as before when the registration fails', async () => {
  container.register.mockRejectedValue(new TypeError('Failed to register'));

  const updates = await registered();

  expect(updates.waiting()).toBe(false);
});

it('checks for a new version at start', async () => {
  await registered();

  expect(registration.update).toHaveBeenCalledOnce();
});

it('checks again when the page becomes visible an hour after the last check', async () => {
  await registered();
  vi.advanceTimersByTime(hour);

  becomes('visible');

  expect(registration.update).toHaveBeenCalledTimes(2);
});

it('checks no more than once an hour', async () => {
  await registered();
  vi.advanceTimersByTime(hour - 1);
  becomes('visible');
  expect(registration.update).toHaveBeenCalledOnce();

  vi.advanceTimersByTime(1);
  becomes('visible');
  becomes('visible');

  expect(registration.update).toHaveBeenCalledTimes(2);
});

it('checks only when the page becomes visible, not when it is hidden', async () => {
  await registered();
  vi.advanceTimersByTime(hour);

  becomes('hidden');

  expect(registration.update).toHaveBeenCalledOnce();
});

it('carries on when a check cannot reach the server', async () => {
  registration.update.mockRejectedValue(new TypeError('Failed to fetch'));
  await registered();
  vi.advanceTimersByTime(hour);

  becomes('visible');
  await vi.advanceTimersByTimeAsync(0);

  expect(registration.update).toHaveBeenCalledTimes(2);
});

it('offers a version that was already waiting at start', async () => {
  registration.waiting = new FakeWorker();

  const updates = await registered();

  expect(updates.waiting()).toBe(true);
});

it('offers a version found while the app is open, once it has installed', async () => {
  const updates = await registered();
  const changed = vi.fn();
  updates.subscribe(changed);

  const worker = registration.found();
  expect(updates.waiting()).toBe(false);
  registration.installed(worker);

  expect(updates.waiting()).toBe(true);
  expect(changed).toHaveBeenCalledOnce();
});

it('offers nothing for a version that failed to install', async () => {
  const updates = await registered();
  const changed = vi.fn();
  updates.subscribe(changed);

  registration.found().becomes('redundant');

  expect(updates.waiting()).toBe(false);
  expect(changed).not.toHaveBeenCalled();
});

it('offers a version the browser was already installing when the registration resolved', async () => {
  const worker = new FakeWorker();
  registration.installing = worker;
  const updates = await registered();

  registration.installed(worker);

  expect(updates.waiting()).toBe(true);
});

it('offers nothing to a page no version controls, as nothing would take it over', async () => {
  container.controller = null;
  registration.waiting = new FakeWorker();

  const updates = await registered();
  registration.installed(registration.found());

  expect(updates.waiting()).toBe(false);
});

it('tells no one once they stop listening', async () => {
  const updates = await registered();
  const changed = vi.fn();
  updates.subscribe(changed)();

  registration.installed(registration.found());

  expect(changed).not.toHaveBeenCalled();
});

it('offers nothing on the first visit, when the first version takes charge by itself', async () => {
  serveWith(new FakeContainer(Object.assign(new FakeRegistration(), { active: null })));
  const updates = await registered();

  const worker = registration.found();
  registration.installed(worker);
  registration.activated(worker);

  expect(updates.waiting()).toBe(false);
});

it('never activates a waiting version by itself', async () => {
  const updates = await registered();
  const worker = registration.found();
  registration.installed(worker);
  vi.advanceTimersByTime(24 * hour);
  becomes('visible');

  expect(updates.waiting()).toBe(true);
  expect(worker.postMessage).not.toHaveBeenCalled();
  expect(reload).not.toHaveBeenCalled();
});

it('asks the waiting version to take over, and reloads once it has', async () => {
  const updates = await registered();
  const worker = registration.found();
  registration.installed(worker);

  updates.reload();

  expect(worker.postMessage).toHaveBeenCalledExactlyOnceWith({ type: 'SKIP_WAITING' });
  expect(reload).not.toHaveBeenCalled();
  container.takenOverBy(worker);
  expect(reload).toHaveBeenCalledOnce();
});

it('does not reload when another tab applied the new version', async () => {
  await registered();
  const worker = registration.found();
  registration.installed(worker);

  container.takenOverBy(worker);

  expect(reload).not.toHaveBeenCalled();
});

it('reloads at once when another tab already applied the new version', async () => {
  const updates = await registered();
  const worker = registration.found();
  registration.installed(worker);
  container.takenOverBy(worker);

  updates.reload();

  expect(worker.postMessage).not.toHaveBeenCalled();
  expect(reload).toHaveBeenCalledOnce();
});

it('reloads at once when no version is waiting', async () => {
  const updates = await registered();

  updates.reload();

  expect(reload).toHaveBeenCalledOnce();
});

it('reloads at once a page no version controls, as nothing would take it over', async () => {
  container.controller = null;
  const worker = new FakeWorker();
  registration.waiting = worker;
  const updates = await registered();

  updates.reload();

  expect(worker.postMessage).not.toHaveBeenCalled();
  expect(reload).toHaveBeenCalledOnce();
});
