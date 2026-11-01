import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderApp } from '../../test/render-app';

describe('the signed-in placeholder', () => {
  it('sends a visitor without a token to the login screen', async () => {
    const { router } = renderApp('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/login');
    expect(screen.queryByText("You're signed in. Your photo library is on its way.")).toBeNull();
  });
});
