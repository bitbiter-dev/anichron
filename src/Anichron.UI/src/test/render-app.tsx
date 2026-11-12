import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';
import { createApiClient } from '../api/client';
import type { Tabs } from '../api/tabs';
import { App } from '../app/App';
import { createQueryClient } from '../app/query-client';
import { createAppRouter } from '../app/router';
import { createI18n } from '../i18n/i18n';
import { track } from './query-clients';
import { sharedTabs } from './tabs';

import.meta.glob(['../features/*/*Page.tsx', '../api/event-stream.ts'], { eager: true });

export function renderApp(path: string, tabs: Tabs = sharedTabs().tab()) {
  const router = createAppRouter(
    createApiClient(tabs),
    track(createQueryClient()),
    createMemoryHistory({ initialEntries: [path] }),
  );
  const i18n = createI18n();
  return { ...render(<App router={router} i18n={i18n} />), router, i18n };
}
