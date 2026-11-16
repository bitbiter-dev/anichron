import { serveAppShell, type PrecacheManifest, type WorkerScope } from './app-shell';

declare const self: WorkerScope & { __WB_MANIFEST: PrecacheManifest };

serveAppShell(self, self.__WB_MANIFEST);
