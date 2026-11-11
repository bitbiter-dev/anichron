import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nextProvider } from 'react-i18next';
import tokens from '../shared/ui/tokens.css?raw';
import { createI18n } from '../i18n/i18n';
import { DesignPage } from './DesignPage';
import './design.css';

const root = document.getElementById('root');
if (root) {
  createRoot(root).render(
    <StrictMode>
      <I18nextProvider i18n={createI18n()}>
        <DesignPage tokens={tokens} />
      </I18nextProvider>
    </StrictMode>,
  );
}
