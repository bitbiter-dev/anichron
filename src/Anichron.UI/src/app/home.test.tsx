import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import { noPhotosSession, refreshSucceeds } from '../test/handlers/auth';
import { onThisDayAnswers } from '../test/handlers/flashbacks';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const message = 'No photos are set up for you yet.';
const user = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
const notice = () => screen.findByRole('alertdialog', { name: message });

let asked: string[];

beforeEach(() => {
  asked = [];
  vi.setSystemTime(new Date(2026, 10, 11, 8));
  server.use(
    refreshSucceeds(noPhotosSession),
    onThisDayAnswers((date) => {
      asked.push(date);
      return [];
    }),
  );
});

describe('the home screen without a storage config', () => {
  it('tells the user no photos are set up and asks for no memories', async () => {
    renderApp('/');

    const dialog = await notice();
    expect(within(dialog).getByText('An admin can add a storage folder for you.')).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'On This Day' })).not.toBeInTheDocument();
    expect(asked).toEqual([]);
  });

  it('takes focus and has no accessibility violations', async () => {
    const { container } = renderApp('/');

    const dialog = await notice();
    await act(() => vi.advanceTimersToNextFrame());

    expect(dialog).toContainElement(document.activeElement as HTMLElement);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('closes on OK and stays closed after a visit to settings', async () => {
    renderApp('/');
    const dialog = await notice();

    await user().click(within(dialog).getByRole('button', { name: 'OK' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    await user().click(screen.getByRole('link', { name: 'Settings' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    await user().click(screen.getByRole('link', { name: 'Today' }));

    await vi.waitFor(() => {
      expect(screen.queryByRole('heading', { level: 1, name: 'Settings' })).not.toBeInTheDocument();
    });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(asked).toEqual([]);
  });

  it('stays open after a click outside it', async () => {
    renderApp('/');
    await notice();

    await user().click(document.body);

    expect(await notice()).toBeVisible();
  });

  it('opens again on the next app start', async () => {
    const first = renderApp('/');
    await user().click(within(await notice()).getByRole('button', { name: 'OK' }));
    first.unmount();

    renderApp('/');

    expect(await notice()).toBeVisible();
  });

  it('takes a link to a day to the home screen', async () => {
    const { router } = renderApp('/day/2026-11-11?year=2023');

    expect(await notice()).toBeVisible();
    expect(router.state.location.pathname).toBe('/');
    expect(asked).toEqual([]);
  });
});
