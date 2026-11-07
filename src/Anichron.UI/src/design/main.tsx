import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import tokens from '../app/tokens.css?raw';
import { DesignPage } from './DesignPage';
import './design.css';

const root = document.getElementById('root');
if (root) {
  createRoot(root).render(
    <StrictMode>
      <DesignPage tokens={tokens} />
    </StrictMode>,
  );
}
