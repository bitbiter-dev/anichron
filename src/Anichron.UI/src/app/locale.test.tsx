import { screen } from '@testing-library/react';
import { act } from 'react';
import { describe, expect, it } from 'vitest';
import { renderApp } from '../test/render-app';

describe('the page language', () => {
  it('follows the app’s language', async () => {
    const { i18n } = renderApp('/login');
    await screen.findByRole('heading', { level: 1 });

    expect(document.documentElement).toHaveAttribute('lang', 'en');
    await act(() => i18n.changeLanguage('de'));
    expect(document.documentElement).toHaveAttribute('lang', 'de');
  });
});
