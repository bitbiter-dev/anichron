import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { z } from 'zod/mini';
import { createApiClient } from '../api/client';
import { createI18n } from '../i18n/i18n';
import { App } from './App';
import './base.css';
import { createAppRouter } from './router';

// The CSP forbids the eval that zod probes for.
z.config({ jitless: true });

const root = document.getElementById('root');
if (root) {
  createRoot(root).render(
    <StrictMode>
      <App router={createAppRouter(createApiClient())} i18n={createI18n()} />
    </StrictMode>,
  );
}
