import { fireEvent, screen } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import {
  loginRespondsWith,
  logoutRespondsWith,
  logoutSucceeds,
  miraSession,
  problemResponse,
  refreshRespondsWith,
  refreshSucceeds,
} from '../test/handlers/auth';
import { everyoneHasMemories, memoriesOf } from '../test/handlers/flashbacks';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const findLoginHeading = () =>
  screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

beforeEach(() => {
  server.use(everyoneHasMemories());
});

async function openSignedIn() {
  const rendered = renderApp('/');
  expect(await screen.findByRole('link', memoriesOf('mira'))).toBeVisible();
  fireEvent.click(screen.getByRole('link', { name: 'Settings' }));
  expect(await screen.findByRole('button', { name: 'Sign out' })).toBeVisible();
  return rendered;
}

const signOut = () => {
  fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
};

describe('signing out', () => {
  it('ends the session on the server, forgets it and lands on the login screen', async () => {
    const bearers: (string | null)[] = [];
    server.use(
      refreshSucceeds(),
      logoutSucceeds((bearer) => bearers.push(bearer)),
    );
    const { router, container } = await openSignedIn();
    expect(await axeResults(container)).toHaveNoViolations();

    signOut();

    expect(await findLoginHeading()).toBeVisible();
    expect(bearers).toEqual([`Bearer ${miraSession.accessToken}`]);
    expect(screen.queryByRole('link', memoriesOf('mira'))).toBeNull();
    await act(() => router.navigate({ to: '/' }));
    expect(router.state.location.pathname).toBe('/login');
  });

  it('renews an expired access token first, so the server ends the session', async () => {
    let refreshes = 0;
    const bearers: (string | null)[] = [];
    server.use(
      refreshRespondsWith(() =>
        HttpResponse.json({ ...miraSession, accessToken: `token-${String(++refreshes)}` }),
      ),
      logoutRespondsWith(() => {
        bearers.push(`token-${String(refreshes)}`);
        return refreshes === 1
          ? problemResponse(401, 'unauthorized')
          : new HttpResponse(null, { status: 204 });
      }),
    );
    await openSignedIn();

    signOut();

    expect(await findLoginHeading()).toBeVisible();
    expect(bearers).toEqual(['token-1', 'token-2']);
  });

  it.each([
    ['answered 401', 401, 'refresh_token_invalid'],
    ['unavailable', 503, 'service_unavailable'],
  ] as const)(
    'signs out without asking the server again when the refresh after a 401 is %s',
    async (_, status, code) => {
      let refreshes = 0;
      let logouts = 0;
      server.use(
        refreshRespondsWith(() =>
          ++refreshes === 1 ? HttpResponse.json(miraSession) : problemResponse(status, code),
        ),
        logoutRespondsWith(() => {
          logouts++;
          return problemResponse(401, 'unauthorized');
        }),
      );
      await openSignedIn();

      signOut();

      expect(await findLoginHeading()).toBeVisible();
      expect(refreshes).toBe(2);
      expect(logouts).toBe(1);
    },
  );

  it('stops renewing the session it ended', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() => {
        refreshes++;
        return HttpResponse.json(miraSession);
      }),
      logoutSucceeds(),
    );
    await openSignedIn();

    signOut();
    expect(await findLoginHeading()).toBeVisible();

    await act(() => vi.advanceTimersByTimeAsync(Number(miraSession.expiresIn) * 1000));
    expect(refreshes).toBe(1);
    expect(screen.getByRole('heading', { level: 1, name: 'Sign in to Anichron' })).toBeVisible();
  });

  it('signs out this tab even when the server cannot be reached', async () => {
    server.use(
      refreshSucceeds(),
      logoutRespondsWith(() => HttpResponse.error()),
    );
    const { router } = await openSignedIn();

    signOut();

    expect(await findLoginHeading()).toBeVisible();
    await act(() => router.navigate({ to: '/' }));
    expect(router.state.location.pathname).toBe('/login');
  });

  it('leaves nothing of the previous user for the next one to see', async () => {
    const noor = { ...miraSession.user, username: 'noor', email: 'noor@example.com' };
    server.use(
      refreshSucceeds(),
      logoutSucceeds(),
      loginRespondsWith(() =>
        HttpResponse.json({ ...miraSession, accessToken: 'token-noor', user: noor }),
      ),
    );
    await openSignedIn();
    signOut();
    expect(await findLoginHeading()).toBeVisible();

    fireEvent.change(screen.getByLabelText('Username or email'), { target: { value: 'noor' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'hunter2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('button', { name: 'Sign out' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'On This Day' })).toBeVisible();

    expect(screen.queryByRole('link', memoriesOf('mira'))).toBeNull();
    expect(screen.getByRole('link', memoriesOf('noor'))).toBeVisible();
  });
});
