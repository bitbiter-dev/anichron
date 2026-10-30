import { RouterProvider } from '@tanstack/react-router';
import type { i18n } from 'i18next';
import { I18nextProvider } from 'react-i18next';
import type { createAppRouter } from './router';

interface AppProps {
  router: ReturnType<typeof createAppRouter>;
  i18n: i18n;
}

export function App({ router, i18n }: AppProps) {
  return (
    <I18nextProvider i18n={i18n}>
      <RouterProvider router={router} />
    </I18nextProvider>
  );
}
