import { PrecacheController, PrecacheRoute, type PrecacheEntry } from 'workbox-precaching';
import { Route, Router } from 'workbox-routing';
import { asksToTakeOver } from './take-over';

interface Lifecycle {
  waitUntil(promise: Promise<unknown>): void;
}

interface Fetch extends Lifecycle {
  request: Request;
  respondWith(response: Promise<Response>): void;
}

interface Message {
  data: unknown;
}

export type PrecacheManifest = (string | PrecacheEntry)[];

export interface WorkerScope {
  addEventListener(type: 'install' | 'activate', listener: (event: Lifecycle) => void): void;
  addEventListener(type: 'fetch', listener: (event: Fetch) => void): void;
  addEventListener(type: 'message', listener: (event: Message) => void): void;
  skipWaiting(): Promise<void>;
}

function isAppPage(path: string) {
  return !/^\/api(\/|$)/i.test(path) && !/\.[^/]*$/.test(path);
}

export function serveAppShell(scope: WorkerScope, manifest: PrecacheManifest) {
  const precache = new PrecacheController();
  precache.addToCacheList(manifest);
  const router = new Router();
  router.registerRoute(new PrecacheRoute(precache));
  router.registerRoute(
    new Route(
      ({ request, url }) => request.mode === 'navigate' && isAppPage(url.pathname),
      precache.createHandlerBoundToURL('/index.html'),
    ),
  );
  scope.addEventListener('install', (event) => {
    void precache.install(event);
  });
  scope.addEventListener('activate', (event) => {
    void precache.activate(event);
  });
  scope.addEventListener('fetch', (event) => {
    const response = router.handleRequest({ request: event.request, event });
    if (response) event.respondWith(response);
  });
  scope.addEventListener('message', (event) => {
    if (asksToTakeOver(event.data)) void scope.skipWaiting();
  });
}
