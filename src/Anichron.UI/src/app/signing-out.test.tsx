import { fireEvent, screen } from '@testing-library/react';
import { delay, HttpResponse } from 'msw';
import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import {
  loginRespondsWith,
  logoutRespondsWith,
  logoutSucceeds,
  meAnswersFor,
  meRespondsWith,
  miraSession,
  problemResponse,
  refreshRespondsWith,
  refreshSucceeds,
} from '../test/handlers/auth';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const greeting = 'Signed in as mira.';
const findLoginHeading = () =>
  screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

async function openSignedIn() {
  const rendered = renderApp('/');
  expect(await screen.findByText(greeting)).toBeVisible();
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
    expect(screen.queryByText(greeting)).toBeNull();
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
      meAnswersFor(() => true),
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
      meRespondsWith(async (bearer) => {
        if (bearer !== 'Bearer token-noor') return HttpResponse.json(miraSession.user);
        await delay(1000);
        return HttpResponse.json(noor);
      }),
    );
    await openSignedIn();
    signOut();
    expect(await findLoginHeading()).toBeVisible();

    fireEvent.change(screen.getByLabelText('Username or email'), { target: { value: 'noor' } });
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'hunter2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(
      await screen.findByText("You're signed in. Your photo library is on its way."),
    ).toBeVisible();

    expect(screen.queryByText(greeting)).toBeNull();
    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(await screen.findByText('Signed in as noor.')).toBeVisible();
  });
});
