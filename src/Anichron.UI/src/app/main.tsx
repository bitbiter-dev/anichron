import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createI18n } from '../i18n/i18n';
import { App } from './App';
import './base.css';
import { createAppRouter } from './router';

const root = document.getElementById('root');
if (root) {
  createRoot(root).render(
    <StrictMode>
      <App router={createAppRouter()} i18n={createI18n()} />
    </StrictMode>,
  );
}
