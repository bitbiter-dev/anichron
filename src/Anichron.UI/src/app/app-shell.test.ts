import { beforeEach, expect, it, vi } from 'vitest';
import { memoryCaches } from '../test/caches';
import { serveAppShell, type WorkerScope } from './app-shell';

const frame = '<!doctype html><title>Anichron</title>';

const files: Record<string, string> = {
  '/index.html': frame,
  '/theme-init.js': 'applyTheme();',
  '/assets/index-abc123.js': 'render();',
  '/assets/index-old456.js': 'renderBefore();',
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

async function settled<T>(promise: Promise<T>) {
  const progress = { done: false };
  const result = promise.finally(() => (progress.done = true));
  for (let round = 0; round < 10 && !progress.done; round++) await vi.runAllTimersAsync();
  expect(progress.done).toBe(true);
  return result;
}

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
  answer: Promise<Response | undefined> | undefined;

  constructor(request: Request) {
    super('fetch');
    this.request = request;
  }

  respondWith(response: Promise<Response>) {
    this.answer = Promise.resolve(response);
  }
}

function serviceWorker() {
  const listeners = new Map<string, (event: never) => void>();
  const skipWaiting = vi.fn(() => Promise.resolve());
  const scope: WorkerScope = {
    addEventListener(type: string, listener: (event: never) => void) {
      listeners.set(type, listener);
    },
    skipWaiting,
  };
  const dispatch = async (event: ExtendableEvent) => {
    listeners.get(event.type)?.(event as never);
    await settled(Promise.all(event.pending));
  };
  return {
    scope,
    skipWaiting,
    message: (data: unknown) => dispatch(Object.assign(new ExtendableEvent('message'), { data })),
    lifecycle: (type: 'install' | 'activate') => dispatch(new ExtendableEvent(type)),
    async served(request: Request) {
      const event = new FetchEvent(request);
      await dispatch(event);
      if (!event.answer) return 'network';
      return (await settled(event.answer))?.text();
    },
  };
}

async function installed(entries = manifest()) {
  vi.stubGlobal('fetch', (request: Request) => {
    const body = files[new URL(request.url).pathname];
    return body === undefined
      ? Promise.reject(new TypeError('offline'))
      : Promise.resolve(new Response(body));
  });
  const worker = serviceWorker();
  serveAppShell(worker.scope, entries);
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

it.each(['/', '/login', '/day/2024-11-16', '/apiary', '/memories/api', '/admin.x/work-items'])(
  'answers a navigation to the app page %s with the precached frame',
  async (path) => {
    const worker = await installed();

    expect(await worker.served(navigation(path))).toBe(frame);
  },
);

it.each(['/theme-init.js', '/assets/index-abc123.js'])(
  'answers a request for %s from the precache',
  async (path) => {
    const worker = await installed();

    expect(await worker.served(new Request(url(path)))).toBe(files[path]);
  },
);

it.each([
  '/api',
  '/api/',
  '/API/v1/session',
  '/Api',
  '/api/v1/session/refresh',
  '/api/v1/flashbacks?date=2024-11-16',
])('leaves a navigation to %s to the network', async (path) => {
  const worker = await installed();

  expect(await worker.served(navigation(path))).toBe('network');
});

it.each(['/api/v1/session/refresh', '/api/v1/proxies/1/thumbnail?v=abc'])(
  'leaves a request for %s to the network',
  async (path) => {
    const worker = await installed();

    expect(await worker.served(new Request(url(path), { method: 'POST' }))).toBe('network');
    expect(await worker.served(new Request(url(path)))).toBe('network');
  },
);

it.each(['/icon.svg', '/manifest.webmanifest', '/day/2024.11'])(
  'leaves a navigation to the file %s to the network, as the server never answers it with the frame',
  async (path) => {
    const worker = await installed();

    expect(await worker.served(navigation(path))).toBe('network');
  },
);

it('leaves a request for a file it did not precache to the network', async () => {
  const worker = await installed();

  expect(await worker.served(new Request(url('/icon-192.png')))).toBe('network');
});

it('leaves a request for an app page that is not a navigation to the network', async () => {
  const worker = await installed();

  expect(await worker.served(new Request(url('/login')))).toBe('network');
});

it('drops the files an earlier version precached once it activates', async () => {
  const cachedPaths = async () => {
    const [name] = await caches.keys();
    const requests = await (await caches.open(name as string)).keys();
    return requests.map((request) => new URL(request.url).pathname);
  };
  await installed([...manifest(), { url: 'assets/index-old456.js', revision: null }]);
  expect(await cachedPaths()).toContain('/assets/index-old456.js');

  await installed();

  expect(await cachedPaths()).not.toContain('/assets/index-old456.js');
});

it('waits after installing, with the previous version still in charge', async () => {
  const worker = await installed();

  expect(worker.skipWaiting).not.toHaveBeenCalled();
});

it('takes over when a page asks it to', async () => {
  const worker = await installed();

  await worker.message({ type: 'SKIP_WAITING' });

  expect(worker.skipWaiting).toHaveBeenCalledOnce();
});

it.each([undefined, null, 'SKIP_WAITING', { type: 'skip_waiting' }, {}])(
  'keeps waiting on any other message (%o)',
  async (data) => {
    const worker = await installed();

    await worker.message(data);

    expect(worker.skipWaiting).not.toHaveBeenCalled();
  },
);
