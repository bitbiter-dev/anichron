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
import { becomes } from '../test/visibility';

const signedIn = { level: 1, name: 'On This Day' } as const;
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

function answers(respond: (refresh: number) => Response | Promise<Response>) {
  let refreshes = 0;
  server.use(refreshRespondsWith(() => respond(++refreshes)));
  return () => refreshes;
}

async function retriesAfter(seconds: number, refreshes: () => number) {
  const before = refreshes();
  await act(() => vi.advanceTimersByTimeAsync(seconds * 1000 - 1));
  expect(refreshes()).toBe(before);
  await act(() => vi.advanceTimersByTimeAsync(1));
  expect(refreshes()).toBe(before + 1);
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
    expect(screen.queryByRole('heading', signedIn)).toBeNull();
    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();

    await act(() => vi.advanceTimersByTimeAsync(300));
    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
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
      expect(screen.queryByRole('heading', signedIn)).toBeNull();
      expect(await axeResults(container)).toHaveNoViolations();

      server.use(loginSucceeds());
      signIn();

      expect(await screen.findByRole('heading', signedIn)).toBeVisible();
      expect(where(router)).toBe('/?from=link');
    },
  );

  it.each([
    ['cannot be reached', () => HttpResponse.error()],
    ['is unavailable', () => problemResponse(503, 'service_unavailable')],
  ])(
    'says so when the server %s, and a retry by itself lands on the requested page',
    async (_, failure) => {
      const refreshes = answers((n) => (n === 1 ? failure() : HttpResponse.json(miraSession)));
      const { router } = renderApp('/?from=link');

      expect(await findUnreachableHeading()).toBeVisible();
      expect(screen.getByText(unreachableBody)).toBeVisible();
      expect(screen.queryByText(/You can try again/)).toBeNull();
      expect(loginHeading()).toBeNull();
      expect(where(router)).toBe('/?from=link');

      await retriesAfter(5, refreshes);

      expect(await screen.findByRole('heading', signedIn)).toBeVisible();
      expect(unreachableHeading()).toBeNull();
      expect(where(router)).toBe('/?from=link');
      expect(refreshes()).toBe(2);
    },
  );

  it('retries by itself after 5, 10, 20, 40, then every 60 seconds', async () => {
    const refreshes = answers(() => HttpResponse.error());
    renderApp('/');
    await findUnreachableHeading();

    for (const seconds of [5, 10, 20, 40, 60, 60]) await retriesAfter(seconds, refreshes);

    expect(unreachableHeading()).toBeVisible();
  });

  it('retries nothing while the page is hidden, then at once when it is visible again, starting over at 5 seconds', async () => {
    const refreshes = answers(() => HttpResponse.error());
    renderApp('/');
    await findUnreachableHeading();
    await retriesAfter(5, refreshes);
    await retriesAfter(10, refreshes);

    becomes('hidden');
    await act(() => {
      window.dispatchEvent(new Event('online'));
      return vi.advanceTimersByTimeAsync(10 * 60_000);
    });
    expect(refreshes()).toBe(3);

    await act(() => {
      becomes('visible');
      return vi.advanceTimersByTimeAsync(0);
    });
    expect(refreshes()).toBe(4);
    await retriesAfter(5, refreshes);
  });

  it('retries by itself when the browser comes back online', async () => {
    const refreshes = answers((n) =>
      n === 1 ? HttpResponse.error() : HttpResponse.json(miraSession),
    );
    const { router } = renderApp('/settings?from=link');
    await findUnreachableHeading();

    await act(() => {
      window.dispatchEvent(new Event('online'));
      return vi.advanceTimersByTimeAsync(0);
    });

    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
    expect(where(router)).toBe('/settings?from=link');
    expect(refreshes()).toBe(2);
  });

  it('waits out Retry-After from a 503 before retrying by itself, then backs off from 5 seconds', async () => {
    const refreshes = answers((n) =>
      n === 1
        ? problemResponse(503, 'service_unavailable', { 'Retry-After': '30' })
        : HttpResponse.error(),
    );
    renderApp('/');

    expect(await findUnreachableHeading()).toBeVisible();
    expect(screen.getByText('You can try again in 30 seconds.')).toBeVisible();
    expect(retryButton()).toBeDisabled();

    await retriesAfter(30, refreshes);
    await retriesAfter(5, refreshes);
  });

  it('shows the button pending while a retry runs, and starts no second one meanwhile', async () => {
    const refreshes = answers(async (n) => {
      if (n === 2) await delay(3000);
      return HttpResponse.error();
    });
    renderApp('/');
    await findUnreachableHeading();
    expect(retryButton()).not.toHaveAttribute('data-pending');

    await act(() => vi.advanceTimersByTimeAsync(5000));
    expect(retryButton()).toHaveAttribute('data-pending', 'true');
    expect(retryButton()).toHaveAttribute('aria-disabled', 'true');
    await act(() => {
      window.dispatchEvent(new Event('online'));
      becomes('visible');
      return vi.advanceTimersByTimeAsync(2000);
    });
    expect(refreshes()).toBe(2);

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(retryButton()).not.toHaveAttribute('data-pending');
    expect(unreachableHeading()).toBeVisible();
    expect(refreshes()).toBe(2);
    await retriesAfter(5, refreshes);
  });

  it('reloads the page when Retry is pressed, pending until it does', async () => {
    const refreshes = answers(() => HttpResponse.error());
    const { updates, container } = renderApp('/');
    await findUnreachableHeading();

    fireEvent.click(retryButton());

    expect(updates.reload).toHaveBeenCalledOnce();
    expect(retryButton()).toHaveAttribute('data-pending', 'true');
    expect(refreshes()).toBe(1);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('goes to the login screen when a retry is answered with an auth problem', async () => {
    const refreshes = answers((n) =>
      n === 1 ? HttpResponse.error() : problemResponse(401, 'refresh_token_invalid'),
    );
    const { router } = renderApp('/?from=link');
    await findUnreachableHeading();

    await retriesAfter(5, refreshes);

    expect(await screen.findByRole('heading', loginHeadingName)).toBeVisible();
    expect(where(router)).toBe('/login?redirect=%2F%3Ffrom%3Dlink');
  });

  it('stops retrying once the screen is gone while it waits', async () => {
    const refreshes = answers(() => HttpResponse.error());
    const { unmount } = renderApp('/');
    await findUnreachableHeading();

    unmount();
    await act(() => {
      window.dispatchEvent(new Event('online'));
      becomes('visible');
      return vi.advanceTimersByTimeAsync(10 * 60_000);
    });

    expect(refreshes()).toBe(1);
  });

  it('stops retrying once the screen is gone during a retry', async () => {
    const refreshes = answers(async (n) => {
      if (n === 2) await delay(3000);
      return HttpResponse.error();
    });
    const { unmount } = renderApp('/');
    await findUnreachableHeading();
    await act(() => vi.advanceTimersByTimeAsync(5000));

    unmount();
    await act(() => vi.advanceTimersByTimeAsync(10 * 60_000));

    expect(refreshes()).toBe(2);
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

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(where(router)).toBe('/');
  });

  it('lands on the start page after signing in without a redirect', async () => {
    server.use(loginSucceeds());
    const { router } = renderApp('/login');
    await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' });

    expect(where(router)).toBe('/login');

    signIn();

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(where(router)).toBe('/');
  });

  it.each([
    ['/login', '/'],
    ['/login?redirect=%2F%3Ffrom%3Dlink', '/?from=link'],
    ['/login?redirect=%2F%2Fevil.example%2F', '/'],
  ])('takes a signed-in user away from %s to %s', async (path, landing) => {
    server.use(refreshSucceeds());
    const { router } = renderApp(path);

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(where(router)).toBe(landing);
    expect(loginHeading()).toBeNull();
  });
});

describe('starting the app while the server limits the rate', () => {
  it('counts down from Retry-After, ignores coming online or visible meanwhile, then retries by itself onto the requested page', async () => {
    const refreshes = answers((n) =>
      n === 1
        ? problemResponse(429, 'rate_limited', { 'Retry-After': '30' })
        : HttpResponse.json(miraSession),
    );
    const { router, container } = renderApp('/?from=link');

    expect(await screen.findByRole('heading', rateLimitedHeadingName)).toBeVisible();
    expect(screen.getByText(rateLimitedBody)).toBeVisible();
    expect(screen.getByText('You can try again in 30 seconds.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
    expect(unreachableHeading()).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();

    await act(() => vi.advanceTimersByTimeAsync(10_000));
    becomes('hidden');
    await act(() => vi.advanceTimersByTimeAsync(10_000));
    await act(() => {
      becomes('visible');
      window.dispatchEvent(new Event('online'));
      return vi.advanceTimersByTimeAsync(9000);
    });
    expect(screen.getByText('You can try again in 1 second.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
    expect(refreshes()).toBe(1);

    await act(() => vi.advanceTimersByTimeAsync(1000));

    expect(await screen.findByRole('heading', signedIn)).toBeVisible();
    expect(where(router)).toBe('/?from=link');
    expect(refreshes()).toBe(2);
  });

  it('says so and counts down again when a retry is limited', async () => {
    const refreshes = answers((n) =>
      n === 1
        ? HttpResponse.error()
        : problemResponse(429, 'rate_limited', { 'Retry-After': '10' }),
    );
    renderApp('/');
    await findUnreachableHeading();

    await retriesAfter(5, refreshes);

    expect(await screen.findByRole('heading', rateLimitedHeadingName)).toBeVisible();
    expect(screen.getByText('You can try again in 10 seconds.')).toBeVisible();
    expect(retryButton()).toBeDisabled();
    await retriesAfter(10, refreshes);
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
