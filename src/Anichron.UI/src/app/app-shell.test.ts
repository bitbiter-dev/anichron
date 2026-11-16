import { beforeEach, describe, expect, it, vi } from 'vitest';
import { memoryCaches } from '../test/caches';
import { serveAppShell, type WorkerScope } from './app-shell';

const frame = '<!doctype html><title>Anichron</title>';

const files: Record<string, string> = {
  '/index.html': frame,
  '/theme-init.js': 'applyTheme();',
  '/assets/index-abc123.js': 'render();',
};

const manifest = () => [
  { url: 'index.html', revision: 'r1' },
  { url: 'theme-init.js', revision: 'r1' },
  { url: 'assets/index-abc123.js', revision: null },
];

const url = (path: string) => new URL(path, location.href).href;

const navigation = (path: string) => {
  const request = new Request(url(path));
  Object.defineProperty(request, 'mode', { value: 'navigate' });
  return request;
};

// Workbox writes to the cache a task later; a hung promise fails at once instead of timing out.
async function settled<T>(promise: Promise<T>) {
  const progress = { done: false };
  const result = promise.finally(() => (progress.done = true));
  for (let round = 0; round < 10 && !progress.done; round++) await vi.runAllTimersAsync();
  expect(progress.done).toBe(true);
  return result;
}

// A worker resolves a relative URL against its location; Node's Request does not.
class WorkerRequest extends Request {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    super(typeof input === 'string' ? new URL(input, location.href) : input, init);
  }
}

class ExtendableEvent extends Event {
  readonly pending: Promise<unknown>[] = [];

  waitUntil(promise: Promise<unknown>) {
    this.pending.push(promise);
  }
}

class FetchEvent extends ExtendableEvent {
  readonly request: Request;
  answer: Promise<Response> | undefined;

  constructor(request: Request) {
    super('fetch');
    this.request = request;
  }

  respondWith(response: Promise<Response>) {
    this.answer = response;
  }
}

function serviceWorker() {
  const listeners = new Map<string, (event: never) => void>();
  const scope: WorkerScope = {
    addEventListener(type: string, listener: (event: never) => void) {
      listeners.set(type, listener);
    },
  };
  const dispatch = async (event: ExtendableEvent) => {
    listeners.get(event.type)?.(event as never);
    await settled(Promise.all(event.pending));
  };
  return {
    scope,
    lifecycle: (type: 'install' | 'activate') => dispatch(new ExtendableEvent(type)),
    async fetch(request: Request) {
      const event = new FetchEvent(request);
      await dispatch(event);
      return event.answer && settled(event.answer);
    },
  };
}

async function installed() {
  vi.stubGlobal('fetch', (request: Request) => {
    const body = files[new URL(request.url).pathname];
    return body === undefined
      ? Promise.reject(new TypeError('offline'))
      : Promise.resolve(new Response(body));
  });
  const worker = serviceWorker();
  serveAppShell(worker.scope, manifest());
  await worker.lifecycle('install');
  await worker.lifecycle('activate');
  vi.stubGlobal('fetch', () => Promise.reject(new TypeError('offline')));
  return worker;
}

beforeEach(() => {
  vi.stubGlobal('Request', WorkerRequest);
  vi.stubGlobal('caches', memoryCaches());
  vi.stubGlobal('ExtendableEvent', ExtendableEvent);
  vi.stubGlobal('FetchEvent', FetchEvent);
  return () => vi.unstubAllGlobals();
});

describe('with the network cut after install', () => {
  it.each(['/', '/login', '/day/2024-11-16', '/apiary', '/memories/api', '/admin.x/work-items'])(
    'answers a navigation to the app page %s with the precached frame',
    async (path) => {
      const worker = await installed();

      expect(await (await worker.fetch(navigation(path)))?.text()).toBe(frame);
    },
  );

  it.each(['/theme-init.js', '/assets/index-abc123.js'])(
    'answers a request for %s from the precache',
    async (path) => {
      const worker = await installed();

      expect(await (await worker.fetch(new Request(url(path))))?.text()).toBe(files[path]);
    },
  );
});

it.each(['/api', '/api/', '/api/v1/session/refresh', '/api/v1/flashbacks?date=2024-11-16'])(
  'leaves a navigation to %s to the network',
  async (path) => {
    const worker = await installed();

    expect(await worker.fetch(navigation(path))).toBeUndefined();
  },
);

it.each(['/api/v1/session/refresh', '/api/v1/proxies/1/thumbnail?v=abc'])(
  'leaves a request for %s to the network',
  async (path) => {
    const worker = await installed();

    expect(await worker.fetch(new Request(url(path), { method: 'POST' }))).toBeUndefined();
    expect(await worker.fetch(new Request(url(path)))).toBeUndefined();
  },
);

it.each(['/icon.svg', '/manifest.webmanifest', '/day/2024.11'])(
  'leaves a navigation to the file %s to the network, as the server never answers it with the frame',
  async (path) => {
    const worker = await installed();

    expect(await worker.fetch(navigation(path))).toBeUndefined();
  },
);

it('leaves a request for a file it did not precache to the network', async () => {
  const worker = await installed();

  expect(await worker.fetch(new Request(url('/icon-192.png')))).toBeUndefined();
});
