import { screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import {
  adminSession,
  loginSucceeds,
  logoutSucceeds,
  noPhotosSession,
  refreshSucceeds,
} from '../../test/handlers/auth';
import { onThisDayAnswers } from '../../test/handlers/flashbacks';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { user } from '../../test/time';

const message = 'No photos are set up for you yet.';
const hint = 'An admin can add a storage folder for you.';
const title = { level: 1, name: 'On This Day' } as const;

const otherWithoutPhotos = {
  ...noPhotosSession,
  user: { ...noPhotosSession.user, id: adminSession.user.id, username: 'alice' },
};

let asked: string[];

async function showsNoPhotos() {
  expect(await screen.findByRole('heading', title)).toBeVisible();
  expect(screen.getByText(message)).toBeVisible();
  expect(screen.getByText(hint)).toBeVisible();
  expect(screen.queryByRole('dialog')).toBeNull();
  expect(screen.queryByRole('alertdialog')).toBeNull();
}

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

describe('Today without a storage config', () => {
  it('says no photos are set up, and asks for no memories', async () => {
    const { container } = renderApp('/');

    await showsNoPhotos();
    expect(await axeResults(container)).toHaveNoViolations();
    expect(asked).toEqual([]);
  });

  it('says it again after a reload', async () => {
    renderApp('/').unmount();

    renderApp('/');

    await showsNoPhotos();
  });

  it('says it again after a visit to settings', async () => {
    renderApp('/');
    await showsNoPhotos();

    await user().click(screen.getByRole('link', { name: 'Settings' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    await user().click(screen.getByRole('link', { name: 'Today' }));

    await showsNoPhotos();
    expect(asked).toEqual([]);
  });

  it('says it to the next user without one, after a sign-out', async () => {
    server.use(logoutSucceeds(), loginSucceeds(undefined, otherWithoutPhotos));
    renderApp('/settings');
    await user().click(await screen.findByRole('button', { name: 'Sign out' }));
    await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

    await user().type(screen.getByLabelText('Username or email'), 'alice');
    await user().type(screen.getByLabelText('Password'), 'secret');
    await user().click(screen.getByRole('button', { name: 'Sign in' }));

    await showsNoPhotos();
    expect(asked).toEqual([]);
  });

  it('gives an admin no hint to ask an admin', async () => {
    server.use(
      refreshSucceeds({ ...noPhotosSession, user: { ...noPhotosSession.user, isAdmin: true } }),
    );
    const { container } = renderApp('/');

    expect(await screen.findByText(message)).toBeVisible();
    expect(screen.queryByText(hint)).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('takes a link to a day to Today', async () => {
    const { router } = renderApp('/day/2026-11-11?year=2023');

    await showsNoPhotos();
    expect(router.state.location.pathname).toBe('/');
    expect(asked).toEqual([]);
  });
});
