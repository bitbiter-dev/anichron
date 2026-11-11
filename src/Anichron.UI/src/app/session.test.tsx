import { fireEvent, screen } from '@testing-library/react';
import { delay, HttpResponse } from 'msw';
import { act } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import {
  loginSucceeds,
  miraSession,
  problemResponse,
  refreshFails,
  refreshRespondsWith,
  refreshSucceeds,
} from '../test/handlers/auth';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';

const signedIn = "You're signed in. Your photo library is on its way.";
const greeting = 'Signed in as mira.';
const loginHeadingName = { level: 1, name: 'Sign in to Anichron' } as const;
const loginHeading = () => screen.queryByRole('heading', loginHeadingName);
const unreachableHeadingName = { level: 1, name: "Can't reach Anichron" } as const;
const findUnreachableHeading = () => screen.findByRole('heading', unreachableHeadingName);
const unreachableHeading = () => screen.queryByRole('heading', unreachableHeadingName);
const rateLimitedHeadingName = { level: 1, name: 'Too many attempts' } as const;
const rateLimitedBody =
  'Anichron is limiting attempts from this device. Wait a moment, then try again.';
const retryButton = () => screen.getByRole('button', { name: 'Retry' });
const unreachableBody = 'Check your connection, or try again in a moment.';

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
    ['cannot be reached', () => HttpResponse.error()],
    ['is unavailable', () => problemResponse(503, 'service_unavailable')],
  ])('says so when the server %s, and Retry lands on the requested page', async (_, failure) => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() => (++refreshes === 1 ? failure() : HttpResponse.json(miraSession))),
    );
    const { router, container } = renderApp('/?from=link');

    expect(await findUnreachableHeading()).toBeVisible();
    expect(screen.getByText(unreachableBody)).toBeVisible();
    expect(loginHeading()).toBeNull();
    expect(where(router)).toBe('/?from=link');
    expect(await axeResults(container)).toHaveNoViolations();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(unreachableHeading()).toBeNull();
    expect(where(router)).toBe('/?from=link');
    expect(refreshes).toBe(2);
  });

  it('counts down a 503 from Retry-After before Retry can be pressed', async () => {
    server.use(
      refreshRespondsWith(() =>
        problemResponse(503, 'service_unavailable', { 'Retry-After': '5' }),
      ),
    );
    renderApp('/');

    expect(await findUnreachableHeading()).toBeVisible();
    expect(screen.getByText('You can try again in 5 seconds.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
  });

  it('keeps saying so while Retry still cannot reach the server', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() => {
        refreshes++;
        return HttpResponse.error();
      }),
    );
    renderApp('/');
    await findUnreachableHeading();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await act(() => vi.advanceTimersByTimeAsync(0));

    expect(await findUnreachableHeading()).toBeVisible();
    expect(refreshes).toBe(2);
  });

  it('retries by itself when the browser comes back online', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() =>
        ++refreshes === 1 ? HttpResponse.error() : HttpResponse.json(miraSession),
      ),
    );
    const { router } = renderApp('/settings?from=link');
    await findUnreachableHeading();

    await act(() => {
      window.dispatchEvent(new Event('online'));
      return vi.advanceTimersByTimeAsync(0);
    });

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    expect(where(router)).toBe('/settings?from=link');
    expect(refreshes).toBe(2);
  });

  it('goes to the login screen when Retry is answered with an auth problem', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() =>
        ++refreshes === 1 ? HttpResponse.error() : problemResponse(401, 'refresh_token_invalid'),
      ),
    );
    const { router } = renderApp('/?from=link');
    await findUnreachableHeading();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('heading', loginHeadingName)).toBeVisible();
    expect(where(router)).toBe('/login?redirect=%2F%3Ffrom%3Dlink');
  });

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
    expect(await screen.findByText(greeting)).toBeVisible();
    expect(where(router)).toBe(landing);
    expect(loginHeading()).toBeNull();
  });
});

describe('starting the app while the server limits the rate', () => {
  it('counts down from Retry-After, ignores coming online meanwhile, then Retry lands on the requested page', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() =>
        ++refreshes === 1
          ? problemResponse(429, 'rate_limited', { 'Retry-After': '30' })
          : HttpResponse.json(miraSession),
      ),
    );
    const { router, container } = renderApp('/?from=link');

    expect(await screen.findByRole('heading', rateLimitedHeadingName)).toBeVisible();
    expect(screen.getByText(rateLimitedBody)).toBeVisible();
    expect(screen.getByText('You can try again in 30 seconds.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
    expect(unreachableHeading()).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();

    await act(() => vi.advanceTimersByTimeAsync(29_000));
    expect(screen.getByText('You can try again in 1 second.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
    await act(() => {
      window.dispatchEvent(new Event('online'));
      return vi.advanceTimersByTimeAsync(0);
    });
    expect(refreshes).toBe(1);

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(retryButton()).toBeEnabled();

    fireEvent.click(retryButton());

    expect(await screen.findByText(signedIn)).toBeVisible();
    expect(where(router)).toBe('/?from=link');
    expect(refreshes).toBe(2);
  });

  it('counts down again when Retry is limited again', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() =>
        problemResponse(429, 'rate_limited', { 'Retry-After': ++refreshes === 1 ? '1' : '10' }),
      ),
    );
    renderApp('/');
    await screen.findByRole('heading', rateLimitedHeadingName);
    await act(() => vi.advanceTimersByTimeAsync(1000));

    fireEvent.click(retryButton());

    expect(await screen.findByText('You can try again in 10 seconds.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
    expect(refreshes).toBe(2);
  });

  it.each([
    ['without Retry-After', {}],
    ['with an unreadable Retry-After', { 'Retry-After': 'soon' }],
  ])('lets Retry be pressed at once %s', async (_, headers) => {
    server.use(refreshRespondsWith(() => problemResponse(429, 'rate_limited', headers)));
    renderApp('/');

    expect(await screen.findByRole('heading', rateLimitedHeadingName)).toBeVisible();
    expect(screen.queryByText(/You can try again/)).toBeNull();
    expect(retryButton()).toBeEnabled();
  });
});
