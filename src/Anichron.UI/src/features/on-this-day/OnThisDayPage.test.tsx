import { fireEvent, screen } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import {
  problemResponse,
  refreshRespondsWith,
  refreshSucceeds,
  miraSession,
} from '../../test/handlers/auth';
import { memory, onThisDayAnswers, todayAnd, yearGroup } from '../../test/handlers/flashbacks';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { becomes } from '../../test/visibility';

const ids = {
  first2025: '0199a1b2-c3d4-7e5f-8a9b-000000002025',
  second2025: '0199a1b2-c3d4-7e5f-8a9b-100000002025',
  first2023: '0199a1b2-c3d4-7e5f-8a9b-000000002023',
};
const thumbnail = (id: string) => `/api/v1/proxies/${id}/thumbnail?v=0123456789abcdef`;

let requested: string[];

function today(
  years = [
    yearGroup(2025, memory(ids.first2025, 2025), memory(ids.second2025, 2025)),
    yearGroup(2023, memory(ids.first2023, 2023)),
  ],
) {
  return onThisDayAnswers(
    todayAnd([], (date) => {
      requested.push(date);
      return years;
    }),
  );
}

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

const pictureIn = (element: HTMLElement) => element.querySelector('img');

beforeEach(() => {
  requested = [];
  vi.setSystemTime(new Date(2026, 10, 11, 8));
});

describe('the landing screen', () => {
  it('sends a visitor without a session to the login screen', async () => {
    server.use(today());
    const { router } = renderApp('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/login');
    expect(requested).toEqual([]);
  });

  it("shows today's year groups, newest first, each with its first memory", async () => {
    server.use(refreshSucceeds(), today());
    const { container } = renderApp('/');

    expect(await screen.findByRole('link', { name: '1 year ago · 2025' })).toBeVisible();
    const links = screen.getAllByRole('link', { name: /ago/ });
    expect(links.map((link) => link.textContent)).toEqual([
      '1 year ago · 2025',
      '3 years ago · 2023',
    ]);
    expect(links.map((link) => pictureIn(link)?.getAttribute('src'))).toEqual([
      thumbnail(ids.first2025),
      thumbnail(ids.first2023),
    ]);
    expect(requested).toEqual(['2026-11-11']);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('asks for the date of the phone, not of UTC', async () => {
    vi.setSystemTime(new Date(2026, 10, 12, 0, 30));
    server.use(refreshSucceeds(), today());
    renderApp('/');

    await screen.findByRole('link', { name: '1 year ago · 2025' });
    expect(requested).toEqual(['2026-11-12']);
  });

  it('shows a year group without a thumbnail without a picture', async () => {
    const bare = memory(ids.first2023, 2023, {
      proxies: { thumbnail: null, preview: null, video: null },
    });
    server.use(refreshSucceeds(), today([yearGroup(2023, bare)]));
    renderApp('/');

    expect(pictureIn(await screen.findByRole('link', { name: '3 years ago · 2023' }))).toBeNull();
  });

  it('says so when today has no memories from earlier years', async () => {
    server.use(refreshSucceeds(), today([]));
    const { container } = renderApp('/');

    expect(await screen.findByText('Nothing from this day in earlier years')).toBeVisible();
    expect(screen.queryByRole('link', { name: /ago/ })).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each([
    [
      'cannot be reached',
      () => HttpResponse.error(),
      "Can't reach Anichron. Check your connection.",
    ],
    [
      'is unavailable',
      () => problemResponse(503, 'service_unavailable'),
      "Can't reach Anichron. Check your connection.",
    ],
    ['fails', () => problemResponse(500, 'internal_error'), "Couldn't load this day."],
  ])('says so when the server %s, and Try again loads the day', async (_, failure, message) => {
    let failing = true;
    server.use(
      refreshSucceeds(),
      onThisDayAnswers(
        todayAnd([], (date) => {
          requested.push(date);
          return failing ? failure() : [yearGroup(2023, memory(ids.first2023, 2023))];
        }),
      ),
    );
    const { container } = renderApp('/');
    await vi.waitFor(() => {
      expect(requested).toHaveLength(1);
    });

    await advance(6000);
    expect(requested).toHaveLength(3);
    expect(screen.queryByText(message)).toBeNull();
    await advance(2000);
    expect(requested).toHaveLength(4);
    expect(await screen.findByText(message)).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('link', { name: '3 years ago · 2023' })).toBeVisible();
    expect(screen.queryByText(message)).toBeNull();
    expect(requested).toHaveLength(5);
  });

  it('moves to the new day when it becomes visible after midnight, not while hidden', async () => {
    vi.setSystemTime(new Date(2026, 11, 31, 23, 59));
    server.use(refreshSucceeds(), today());
    renderApp('/');
    await screen.findByRole('link', { name: '1 year ago · 2025' });

    vi.setSystemTime(new Date(2027, 0, 1, 0, 1));
    act(() => {
      becomes('hidden');
    });
    await advance(0);
    expect(requested).toEqual(['2026-12-31']);

    act(() => {
      becomes('visible');
    });

    expect(await screen.findByRole('link', { name: '2 years ago · 2025' })).toBeVisible();
    expect(requested.at(-1)).toBe('2027-01-01');
  });
});

describe('a thumbnail that fails to load', () => {
  let refreshes: number;

  beforeEach(() => {
    refreshes = 0;
    server.use(
      refreshRespondsWith(() => {
        refreshes++;
        return HttpResponse.json(miraSession);
      }),
      today(),
    );
  });

  it('renews the session once and loads again once', async () => {
    renderApp('/');
    const link = await screen.findByRole('link', { name: '1 year ago · 2025' });
    const first = pictureIn(link) as HTMLImageElement;

    fireEvent.error(first);
    await advance(0);
    const second = pictureIn(link) as HTMLImageElement;
    expect(refreshes).toBe(2);
    expect(second).not.toBe(first);
    expect(second.getAttribute('src')).toBe(thumbnail(ids.first2025));

    fireEvent.error(second);
    await advance(0);
    expect(refreshes).toBe(2);
    expect(pictureIn(link)).toBe(second);
  });

  it('shares one renewal with the others failing at the same time', async () => {
    renderApp('/');
    await screen.findByRole('link', { name: '1 year ago · 2025' });

    for (const link of screen.getAllByRole('link', { name: /ago/ }))
      fireEvent.error(pictureIn(link) as HTMLImageElement);
    await advance(0);

    expect(refreshes).toBe(2);
    expect(screen.getAllByRole('link', { name: /ago/ })).toHaveLength(2);
  });
});
