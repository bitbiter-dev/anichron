import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { axeResults } from '../../test/axe';
import { renderApp } from '../../test/render-app';

describe('the placeholder page', () => {
  it('greets the user in English at the root', async () => {
    const { container } = renderApp('/');

    expect(await screen.findByRole('heading', { level: 1, name: 'Anichron' })).toBeVisible();
    expect(screen.getByText('Your photo library is on its way.')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });
});
