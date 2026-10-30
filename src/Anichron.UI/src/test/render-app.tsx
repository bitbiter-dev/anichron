import { createMemoryHistory } from '@tanstack/react-router';
import { render } from '@testing-library/react';
import { App } from '../app/App';
import { createAppRouter } from '../app/router';
import { createI18n } from '../i18n/i18n';

export function renderApp(path: string) {
  const router = createAppRouter(createMemoryHistory({ initialEntries: [path] }));
  return render(<App router={router} i18n={createI18n()} />);
}
