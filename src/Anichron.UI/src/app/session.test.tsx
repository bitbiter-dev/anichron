import { fireEvent, screen } from '@testing-library/react';
import { delay, HttpResponse } from 'msw';
import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import {
  loginSucceeds,
  miraSession,
  problem,
  refreshFails,
  refreshRespondsWith,
  refreshSucceeds,
} from '../test/handlers/auth';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const signedIn = "You're signed in. Your photo library is on its way.";
const loginHeading = () => screen.queryByRole('heading', { level: 1, name: 'Sign in to Anichron' });

function signIn() {
  fireEvent.change(screen.getByLabelText('Username or email'), { target: { value: 'mira' } });
  fireEvent.change(screen.getByLabelText('Password'), {
    target: { value: 'correct horse battery staple' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

function where(router: ReturnType<typeof renderApp>['router']) {
  return router.state.location.href;
}

describe('starting the app', () => {
  it('reloads a signed-in user onto the requested page, never showing the login screen', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(async () => {
        refreshes++;
        await delay(1500);
        return HttpResponse.json(miraSession);
      }),
    );
    const { router, container } = renderApp('/?from=link');

    await act(() => vi.advanceTimersByTimeAsync(1200));
    expect(loginHeading()).toBeNull();
    expect(screen.queryByText(signedIn)).toBeNull();
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(300));
    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(loginHeading()).toBeNull();
    expect(where(router)).toBe('/?from=link');
    expect(await axeResults(container)).toHaveNoViolations();

    await act(() => router.navigate({ to: '/login' }));
    expect(where(router)).toBe('/');
    expect(refreshes).toBe(1);
  });

  it.each([
    ['without a refresh cookie', refreshFails('refresh_token_required')],
    ['with a revoked refresh cookie', refreshFails('refresh_token_invalid')],
    ['when the account was disabled', refreshFails('account_disabled')],
    [
      'when the server is unavailable',
      refreshRespondsWith(() =>
        HttpResponse.json(problem(503, 'service_unavailable'), { status: 503 }),
      ),
    ],
    ['when the server cannot be reached', refreshRespondsWith(() => HttpResponse.error())],
  ])(
    'sends a reload %s to the login screen, then back to the requested page',
    async (_, refresh) => {
      server.use(refresh);
      const { router, container } = renderApp('/?from=link');

      expect(
        await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' }),
      ).toBeVisible();
      expect(where(router)).toBe('/login?redirect=%2F%3Ffrom%3Dlink');
      expect(router.state.location.search).toEqual({ redirect: '/?from=link' });
      expect(screen.queryByText(signedIn)).toBeNull();
      expect(await axeResults(container)).toHaveNoViolations();

      server.use(loginSucceeds());
      signIn();

      expect(await screen.findByText(signedIn)).toBeVisible();
      expect(where(router)).toBe('/?from=link');
    },
  );

  it.each([
    ['another origin', 'https://evil.example/'],
    ['a protocol-relative path', '//evil.example/'],
    ['a backslash path', '/\\evil.example/'],
    ['a relative path', 'evil'],
    ['a number', '42'],
    ['a list holding a path', '["/elsewhere"]'],
  ])('ignores a redirect to %s and lands on the start page', async (_, target) => {
    server.use(loginSucceeds());
    const { router } = renderApp(`/login?redirect=${encodeURIComponent(target)}`);
    await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

    signIn();

    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(where(router)).toBe('/');
  });

  it('lands on the start page after signing in without a redirect', async () => {
    server.use(loginSucceeds());
    const { router } = renderApp('/login');
    await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

    expect(where(router)).toBe('/login');

    signIn();

    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(where(router)).toBe('/');
  });

  it.each([
    ['/login', '/'],
    ['/login?redirect=%2F%3Ffrom%3Dlink', '/?from=link'],
    ['/login?redirect=%2F%2Fevil.example%2F', '/'],
  ])('takes a signed-in user away from %s to %s', async (path, landing) => {
    server.use(refreshSucceeds());
    const { router } = renderApp(path);

    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(where(router)).toBe(landing);
    expect(loginHeading()).toBeNull();
  });
});
