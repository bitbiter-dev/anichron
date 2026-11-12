import { fireEvent, screen } from '@testing-library/react';
import { delay, HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../test/axe';
import {
  loginRespondsWith,
  miraSession,
  problemResponse,
  refreshRespondsWith,
} from '../test/handlers/auth';
import {
  everyoneHasMemories,
  memoriesOf,
  memory,
  onThisDayAnswers,
  withoutRecentDays,
  yearGroup,
} from '../test/handlers/flashbacks';
import { renderApp } from '../test/render-app';
import { server } from '../test/server';
import { becomes } from '../test/visibility';

const lifetime = miraSession.expiresIn;
const renewal = lifetime * 800;
const loginHeadingName = { level: 1, name: 'Sign in to Anichron' } as const;
const findLoginHeading = () => screen.findByRole('heading', loginHeadingName);
const unreachableHeadingName = { level: 1, name: "Can't reach Anichron" } as const;

const tokenNumber = (n: number) => `token-${String(n)}`;

let refreshes: number;
let answeredAt: number;
let bearers: (string | null)[];

const renews = (n: number) => HttpResponse.json({ ...miraSession, accessToken: tokenNumber(n) });

function refreshAnswers(answer: (n: number) => Response | Promise<Response> = renews) {
  return refreshRespondsWith(async () => {
    const response = await answer(++refreshes);
    answeredAt = Date.now();
    return response;
  });
}

const unavailable = () => problemResponse(503, 'service_unavailable');
const rateLimited = (headers: Record<string, string> = {}) =>
  problemResponse(429, 'rate_limited', headers);
const ends = (code: 'refresh_token_invalid' | 'refresh_token_required' | 'account_disabled') =>
  problemResponse(401, code);

const miraDay = () => [yearGroup(2023, memory('0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b', 2023))];
const unauthorized = () => HttpResponse.json({ status: 401 }, { status: 401 });

function dayRespondsWith(
  respond: (bearer: string | null) => ReturnType<typeof miraDay> | Response | Promise<Response>,
) {
  return onThisDayAnswers(
    withoutRecentDays((_, bearer) => {
      bearers.push(bearer);
      return respond(bearer);
    }),
  );
}

function dayAccepts(
  accepts: (token: string) => boolean = (token) => token === tokenNumber(refreshes),
) {
  return dayRespondsWith((bearer) =>
    accepts(bearer?.replace('Bearer ', '') ?? '') ? miraDay() : unauthorized(),
  );
}

function signIn(username: string) {
  fireEvent.change(screen.getByLabelText('Username or email'), { target: { value: username } });
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'hunter2' } });
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }));
}

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

async function untilAnswered(milliseconds: number) {
  await advance(answeredAt + milliseconds - Date.now());
}

async function openSignedIn(path = '/') {
  const rendered = renderApp(path);
  expect(await screen.findByRole('link', memoriesOf('mira'))).toBeVisible();
  return rendered;
}

async function wakeAfter(milliseconds: number, state: DocumentVisibilityState = 'visible') {
  vi.setSystemTime(answeredAt + milliseconds);
  await act(async () => {
    becomes(state);
    await Promise.resolve();
  });
  await advance(0);
}

beforeEach(() => {
  server.use(everyoneHasMemories());
  refreshes = 0;
  bearers = [];
});

describe('a session past its access token', () => {
  it('is refreshed at 80% of its lifetime and stays signed in across several lifetimes', async () => {
    server.use(refreshAnswers());
    const { router, container } = await openSignedIn();

    await untilAnswered(renewal - 1);
    expect(refreshes).toBe(1);

    await untilAnswered(renewal);
    expect(refreshes).toBe(2);

    for (let lifetimes = 0; lifetimes < 3; lifetimes++) await untilAnswered(renewal);
    expect(refreshes).toBe(5);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();
    expect(router.state.location.href).toBe('/');
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('keeps what it already fetched across a refresh', async () => {
    server.use(
      refreshAnswers(),
      dayAccepts(() => true),
    );
    await openSignedIn();
    expect(bearers).toHaveLength(1);

    await untilAnswered(renewal);
    await untilAnswered(renewal);
    expect(refreshes).toBe(3);
    expect(bearers).toHaveLength(1);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();
  });

  it('is refreshed on the lifetime of the session a refresh returned', async () => {
    server.use(
      refreshAnswers((n) =>
        HttpResponse.json({
          ...miraSession,
          accessToken: tokenNumber(n),
          expiresIn: n === 1 ? 900 : 100,
        }),
      ),
    );
    await openSignedIn();
    await advance(renewal);
    expect(refreshes).toBe(2);

    await untilAnswered(80_000 - 1);
    expect(refreshes).toBe(2);
    await untilAnswered(80_000);
    expect(refreshes).toBe(3);
  });

  it('is refreshed at 80% of its lifetime after signing in', async () => {
    server.use(
      refreshAnswers((n) => (n === 1 ? ends('refresh_token_required') : renews(n))),
      loginRespondsWith(() => {
        answeredAt = Date.now();
        return HttpResponse.json({ ...miraSession, accessToken: 'token-1' });
      }),
    );
    renderApp('/login');
    await findLoginHeading();
    signIn('mira');
    expect(await screen.findByRole('link', memoriesOf('mira'))).toBeVisible();

    await untilAnswered(renewal - 1);
    expect(refreshes).toBe(1);
    await untilAnswered(renewal);
    expect(refreshes).toBe(2);
  });
});

describe('becoming visible', () => {
  it('refreshes once when the scheduled refresh was missed, and reschedules', async () => {
    server.use(refreshAnswers());
    await openSignedIn();
    await advance(renewal / 2);

    await wakeAfter(renewal + 1);
    expect(refreshes).toBe(2);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();

    await untilAnswered(renewal - 1);
    expect(refreshes).toBe(2);
    await untilAnswered(renewal);
    expect(refreshes).toBe(3);
  });

  it('refreshes when the access token has already expired', async () => {
    server.use(refreshAnswers());
    await openSignedIn();

    await wakeAfter(lifetime * 5000);
    expect(refreshes).toBe(2);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();
  });

  it('does not refresh with 20% of the lifetime left or more', async () => {
    server.use(refreshAnswers());
    await openSignedIn();

    await wakeAfter(0);
    await wakeAfter(renewal);
    expect(refreshes).toBe(1);
  });

  it('does not refresh when the page is hidden', async () => {
    server.use(refreshAnswers());
    await openSignedIn();

    await wakeAfter(renewal + 1, 'hidden');
    expect(refreshes).toBe(1);
  });

  it('does not refresh once the app is gone', async () => {
    server.use(refreshAnswers());
    const { unmount } = await openSignedIn();
    unmount();

    await wakeAfter(renewal + 1);
    expect(refreshes).toBe(1);
  });

  it('does not refresh once signed out', async () => {
    server.use(refreshAnswers((n) => (n === 1 ? renews(n) : ends('refresh_token_invalid'))));
    await openSignedIn();
    await advance(renewal);
    expect(await findLoginHeading()).toBeVisible();

    await wakeAfter(lifetime * 5000);
    expect(refreshes).toBe(2);
  });
});

describe('an API call answered 401', () => {
  it('is retried once after a refresh', async () => {
    server.use(
      refreshAnswers(),
      dayAccepts((token) => token === tokenNumber(2)),
    );
    const { container } = await openSignedIn();

    expect(bearers).toEqual([`Bearer ${tokenNumber(1)}`, `Bearer ${tokenNumber(2)}`]);
    expect(refreshes).toBe(2);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('signs out when the retry is answered 401 again', async () => {
    server.use(
      refreshAnswers(),
      dayAccepts(() => false),
    );
    const { router } = renderApp('/');

    expect(await findLoginHeading()).toBeVisible();
    expect(router.state.location.href).toBe('/login?redirect=%2F');
    expect(bearers).toEqual([`Bearer ${tokenNumber(1)}`, `Bearer ${tokenNumber(2)}`]);

    await advance(lifetime * 1000);
    expect(refreshes).toBe(2);
    expect(bearers).toHaveLength(2);
    expect(screen.getByRole('heading', loginHeadingName)).toBeVisible();
  });

  it('keeps the session and waits out Retry-After when the refresh after it fails', async () => {
    server.use(
      refreshAnswers((n) => (n === 1 ? renews(n) : rateLimited({ 'Retry-After': '30' }))),
      dayAccepts(() => false),
    );
    const { router } = renderApp('/');
    await advance(100);
    expect(refreshes).toBe(2);

    await untilAnswered(30_000 - 1);
    expect(refreshes).toBe(2);
    expect(bearers).toEqual([`Bearer ${tokenNumber(1)}`]);
    expect(screen.queryByRole('heading', loginHeadingName)).toBeNull();
    expect(router.state.location.href).toBe('/');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeVisible();
  });

  it('is retried with the new token, without another refresh, when one renewed it meanwhile', async () => {
    server.use(
      refreshAnswers(async (n) => {
        if (n === 2) await delay(100);
        return renews(n);
      }),
      dayRespondsWith(async (bearer) => {
        if (bearers.length === 1 || bearer === `Bearer ${tokenNumber(2)}`)
          return HttpResponse.json({ date: '', years: miraDay() });
        await delay(500);
        return unauthorized();
      }),
    );
    await openSignedIn();

    await wakeAfter(renewal + 1);
    await advance(1000);
    expect(refreshes).toBe(2);
    expect(bearers).toEqual([
      `Bearer ${tokenNumber(1)}`,
      `Bearer ${tokenNumber(1)}`,
      `Bearer ${tokenNumber(2)}`,
    ]);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();
  });

  it('is retried by the query when it fails for another reason', async () => {
    server.use(refreshAnswers(), dayRespondsWith(unavailable));
    renderApp('/');
    await advance(60_000);

    expect(bearers).toHaveLength(4);
    expect(refreshes).toBe(1);
  });

  it('signs out, without a retry, when the refresh after it is answered 401', async () => {
    server.use(
      refreshAnswers((n) => (n === 1 ? renews(n) : ends('refresh_token_invalid'))),
      dayAccepts(() => false),
    );
    const { router } = renderApp('/');

    expect(await findLoginHeading()).toBeVisible();
    expect(router.state.location.href).toBe('/login?redirect=%2F');
    expect(bearers).toEqual([`Bearer ${tokenNumber(1)}`]);
    expect(refreshes).toBe(2);
  });
});

describe('several triggers at once', () => {
  it('cause one refresh request', async () => {
    server.use(
      refreshAnswers(async (n) => {
        if (n === 2) await delay(1000);
        return renews(n);
      }),
      dayAccepts((token) => token === tokenNumber(2)),
    );
    renderApp('/');
    await advance(100);
    expect(refreshes).toBe(2);

    await wakeAfter(renewal + 1);
    await advance(1000);
    expect(await screen.findByRole('link', memoriesOf('mira'))).toBeVisible();
    expect(refreshes).toBe(2);
    expect(bearers).toEqual([`Bearer ${tokenNumber(1)}`, `Bearer ${tokenNumber(2)}`]);
  });

  it('share the scheduled refresh in flight', async () => {
    server.use(
      refreshAnswers(async (n) => {
        if (n === 2) await delay(1000);
        return renews(n);
      }),
    );
    await openSignedIn();
    await advance(renewal);
    expect(refreshes).toBe(2);

    await wakeAfter(renewal + 1);
    await advance(1000);
    expect(refreshes).toBe(2);
  });
});

describe('a refresh that fails', () => {
  it.each([
    ['with no network', () => HttpResponse.error()],
    ['with 503', unavailable],
    ['with 429', () => rateLimited()],
  ])('%s keeps the user signed in and is retried', async (_, failure) => {
    server.use(refreshAnswers((n) => (n === 2 ? failure() : renews(n))));
    const { router, container } = await openSignedIn();

    await advance(renewal);
    expect(refreshes).toBe(2);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();
    expect(router.state.location.href).toBe('/');
    expect(await axeResults(container)).toHaveNoViolations();

    await untilAnswered(5000 - 1);
    expect(refreshes).toBe(2);
    await untilAnswered(5000);
    expect(refreshes).toBe(3);

    await untilAnswered(renewal);
    expect(refreshes).toBe(4);
  });

  it('is retried after Retry-After', async () => {
    server.use(refreshAnswers((n) => (n === 2 ? rateLimited({ 'Retry-After': '30' }) : renews(n))));
    await openSignedIn();
    await advance(renewal);

    await untilAnswered(30_000 - 1);
    expect(refreshes).toBe(2);
    await untilAnswered(30_000);
    expect(refreshes).toBe(3);
  });

  it('backs off, doubling up to five minutes, and starts over after a success', async () => {
    const failing = new Set([2, 3, 4, 5, 6, 7, 8, 9, 11]);
    server.use(refreshAnswers((n) => (failing.has(n) ? unavailable() : renews(n))));
    await openSignedIn();
    await advance(renewal);
    expect(refreshes).toBe(2);

    for (const [seconds, count] of [
      [5, 3],
      [10, 4],
      [20, 5],
      [40, 6],
      [80, 7],
      [160, 8],
      [300, 9],
      [300, 10],
    ] as const) {
      await untilAnswered(seconds * 1000 - 1);
      expect(refreshes).toBe(count - 1);
      await untilAnswered(seconds * 1000);
      expect(refreshes).toBe(count);
    }

    await untilAnswered(renewal);
    expect(refreshes).toBe(11);
    await untilAnswered(5000);
    expect(refreshes).toBe(12);
    expect(screen.getByRole('link', memoriesOf('mira'))).toBeVisible();
  });

  it.each(['refresh_token_invalid', 'refresh_token_required', 'account_disabled'] as const)(
    'answered 401 %s signs out and stops refreshing',
    async (code) => {
      server.use(refreshAnswers((n) => (n === 1 ? renews(n) : ends(code))));
      const { router, container } = await openSignedIn();

      await advance(renewal);
      expect(await findLoginHeading()).toBeVisible();
      expect(router.state.location.href).toBe('/login?redirect=%2F');
      expect(await axeResults(container)).toHaveNoViolations();

      await advance(lifetime * 1000);
      expect(refreshes).toBe(2);
    },
  );

  it.each(['refresh_token_invalid', 'refresh_token_required', 'account_disabled'] as const)(
    'answered 401 %s signs out, and the next user never sees the previous one',
    async (code) => {
      server.use(refreshAnswers((n) => (n === 1 ? renews(n) : ends(code))));
      await openSignedIn();
      await advance(renewal);
      await findLoginHeading();

      const alice = {
        ...miraSession.user,
        id: '7c9e6679-7425-40de-944b-e07fc1f90ae7',
        username: 'alice',
      };
      server.use(
        loginRespondsWith(() =>
          HttpResponse.json({ ...miraSession, accessToken: 'alice', user: alice }),
        ),
      );
      signIn('alice');

      await screen.findByRole('heading', { level: 1, name: 'On This Day' });
      expect(screen.queryByRole('link', memoriesOf('mira'))).toBeNull();
      expect(await screen.findByRole('link', memoriesOf('alice'))).toBeVisible();
    },
  );

  it('at start-up says the server cannot be reached and is not retried on a schedule', async () => {
    server.use(refreshAnswers(unavailable));
    renderApp('/');
    await screen.findByRole('heading', unreachableHeadingName);

    await advance(lifetime * 1000);
    expect(refreshes).toBe(1);
  });
});
