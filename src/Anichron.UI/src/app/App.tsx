import { QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import type { i18n } from 'i18next';
import { useEffect } from 'react';
import { I18nextProvider } from 'react-i18next';
import type { createAppRouter } from './router';

interface AppProps {
  router: ReturnType<typeof createAppRouter>;
  i18n: i18n;
}

export function App({ router, i18n }: AppProps) {
  const { api, queryClient } = router.options.context;
  useEffect(() => api.watchVisibility(), [api]);
  return (
    <I18nextProvider i18n={i18n}>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </I18nextProvider>
  );
}
