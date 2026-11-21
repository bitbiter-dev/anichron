import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';
import { createApiClient } from '../api/client';
import type { Tabs } from '../api/tabs';
import { App } from '../app/App';
import { createQueryClient } from '../app/query-client';
import { createAppRouter } from '../app/router';
import { keepInstallPrompt } from '../app/install';
import { createI18n } from '../i18n/i18n';
import { inAct } from './act';
import { memoryUrls } from './object-urls';
import { loadImage } from './pictures';
import { settled, track } from './query-clients';
import { sharedTabs } from './tabs';
import { fakeUpdates } from './updates';

import.meta.glob(['../features/*/*Page.tsx', '../api/event-stream.ts'], { eager: true });

export function renderApp(path: string, tabs: Tabs = sharedTabs().tab()) {
  const updates = fakeUpdates();
  const api = createApiClient(tabs, memoryUrls());
  const { downloads, renewal } = api.media;
  const router = createAppRouter(
    {
      ...api,
      media: {
        loadImage: (url) => settled(loadImage(url)),
        downloads: {
          ...downloads,
          picture: (url) => settled(downloads.picture(url)),
          subscribe: (listener) =>
            downloads.subscribe(() => {
              inAct(listener);
            }),
        },
        renewal: () => {
          const lapse = renewal();
          return (source) => settled(lapse(source));
        },
      },
    },
    track(createQueryClient()),
    updates,
    keepInstallPrompt(),
    createMemoryHistory({ initialEntries: [path] }),
  );
  const i18n = createI18n();
  return { ...render(<App router={router} i18n={i18n} />), router, i18n, updates };
}
