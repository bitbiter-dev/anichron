import type { PrecacheEntry } from 'workbox-precaching';
import { serveAppShell, type WorkerScope } from './app-shell';

declare const self: WorkerScope & { __WB_MANIFEST: (string | PrecacheEntry)[] };

serveAppShell(self, self.__WB_MANIFEST);
