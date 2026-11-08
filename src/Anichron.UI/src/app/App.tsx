import { RouterProvider } from '@tanstack/react-router';
import type { i18n } from 'i18next';
import type { ReactNode } from 'react';
import { I18nProvider } from 'react-aria-components';
import { I18nextProvider } from 'react-i18next';
import type { createAppRouter } from './router';
import { useDocumentLocale } from './use-document-locale';

interface AppProps {
  router: ReturnType<typeof createAppRouter>;
  i18n: i18n;
}

export function App({ router, i18n }: AppProps) {
  return (
    <I18nextProvider i18n={i18n}>
      <Localised>
        <RouterProvider router={router} />
      </Localised>
    </I18nextProvider>
  );
}

function Localised({ children }: { children: ReactNode }) {
  return <I18nProvider locale={useDocumentLocale()}>{children}</I18nProvider>;
}
