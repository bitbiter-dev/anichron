import { fireEvent, screen, waitFor, within } from '@testing-library/react';
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
import {
  hold,
  memory,
  onThisDayAnswers,
  proxyUrl,
  recentDaysAnswers,
  recentDaysOf,
  todayAnd,
  yearGroup,
} from '../../test/handlers/flashbacks';
import { holdProxy, proxyAnswers } from '../../test/handlers/proxies';
import { keptSource } from '../../test/object-urls';
import { askedPictures, holdPicture } from '../../test/pictures';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { becomes } from '../../test/visibility';
import { advance, user } from '../../test/time';

const ids = {
  first2025: '0199a1b2-c3d4-7e5f-8a9b-000000002025',
  second2025: '0199a1b2-c3d4-7e5f-8a9b-100000002025',
  first2023: '0199a1b2-c3d4-7e5f-8a9b-000000002023',
  first2021: '0199a1b2-c3d4-7e5f-8a9b-000000002021',
  yesterday2024: '0199a1b2-c3d4-7e5f-8a9b-100000002024',
};
const thumbnail = (id: string) => proxyUrl(id, 'thumbnail');

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

const pictureIn = (element: HTMLElement) => element.querySelector('img');
const date = 'Wed 11 Nov';
const card = () => screen.getByRole('link', { name: date });
const onScreen = () => card().querySelector('[data-shown] img')?.getAttribute('src');
const fromQueue = (id: string) => keptSource(thumbnail(id));
const threeYears = () => [
  yearGroup(2025, memory(ids.first2025, 2025)),
  yearGroup(2023, memory(ids.first2023, 2023)),
  yearGroup(2021, memory(ids.first2021, 2021)),
];

function reduceMotion() {
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (media) =>
      ({
        matches: media === '(prefers-reduced-motion: reduce)',
        media,
        addEventListener: () => undefined,
        removeEventListener: () => undefined,
      }) as unknown as MediaQueryList,
  );
}
const todayBox = () =>
  screen.getByRole('heading', { level: 1, name: 'On This Day' }).nextElementSibling;

beforeEach(() => {
  requested = [];
  vi.setSystemTime(new Date(2026, 10, 11, 8));
});

describe('the landing screen', () => {
  it.each(['load', 'fail'] as const)(
    "shows the card only once its newest year's picture %ss",
    async (outcome) => {
      const thumbnailHeld = holdPicture(thumbnail(ids.first2025));
      server.use(refreshSucceeds(), today());
      renderApp('/');
      await waitFor(() => {
        expect(requested).toEqual(['2026-11-11']);
      });
      await advance(1000);

      expect(screen.queryByRole('link', { name: date })).toBeNull();
      expect(todayBox()).toHaveAttribute('aria-busy', 'true');
      thumbnailHeld[outcome]();
      expect(await screen.findByRole('link', { name: date })).toBeVisible();
      expect(todayBox()).toHaveAttribute('aria-busy', 'false');
    },
  );

  it("shows the card without waiting for the other years' pictures", async () => {
    holdPicture(thumbnail(ids.first2023));
    server.use(refreshSucceeds(), today());
    renderApp('/');

    expect(await screen.findByRole('link', { name: date })).toBeVisible();
    expect(askedPictures()).toEqual([thumbnail(ids.first2025)]);
  });

  it('marks the day busy, showing none of it, while it loads', async () => {
    const answer = hold();
    server.use(
      refreshSucceeds(),
      onThisDayAnswers(
        todayAnd([], async () => {
          await answer.until;
          return [yearGroup(2023, memory(ids.first2023, 2023))];
        }),
      ),
    );
    const { container } = renderApp('/');
    await screen.findByRole('heading', { level: 1, name: 'On This Day' });
    await advance(1000);

    expect(todayBox()).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByRole('link', { name: date })).toBeNull();
    expect(screen.queryByText('Nothing from this day in earlier years')).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();

    answer.release();

    expect(await screen.findByRole('link', { name: date })).toBeVisible();
    expect(todayBox()).toHaveAttribute('aria-busy', 'false');
  });

  it('shows the card when the newest first memory has no picture, asking for no picture for it', async () => {
    const noPicture = {
      proxies: { thumbnail: null, preview: null, compactPreview: null, video: null },
    };
    server.use(
      refreshSucceeds(),
      today([
        yearGroup(2025, memory(ids.first2025, 2025, noPicture)),
        yearGroup(2023, memory(ids.first2023, 2023)),
      ]),
    );
    renderApp('/');

    expect(await screen.findByRole('link', { name: date })).toBeVisible();
    expect(pictureIn(card())).toBeNull();
    expect(askedPictures()).toEqual([]);
  });

  it('sends a visitor without a session to the login screen', async () => {
    server.use(today());
    const { router } = renderApp('/');

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Sign in to Anichron' }),
    ).toBeVisible();
    expect(router.state.location.pathname).toBe('/login');
    expect(requested).toEqual([]);
  });

  it('keeps the card shown when the day comes back with more years', async () => {
    holdPicture(thumbnail(ids.first2023));
    let years = [yearGroup(2025, memory(ids.first2025, 2025))];
    server.use(refreshSucceeds(), onThisDayAnswers(todayAnd([], () => years)));
    const { router } = renderApp('/');
    expect(await screen.findByRole('link', { name: date })).toBeVisible();

    years = threeYears();
    void router.options.context.queryClient.invalidateQueries({ queryKey: ['on-this-day'] });
    await advance(1000);

    expect(card()).toBeVisible();
    expect(onScreen()).toBe(thumbnail(ids.first2025));
  });

  it("shows one card for the date, on the newest year's first memory", async () => {
    server.use(refreshSucceeds(), today());
    const { container } = renderApp('/');

    expect(await screen.findByRole('link', { name: date })).toBeVisible();
    expect(screen.getAllByRole('link', { name: /Nov/ })).toEqual([card()]);
    expect(card()).toHaveTextContent(/^Wed 11 Nov$/);
    expect(onScreen()).toBe(thumbnail(ids.first2025));
    expect(requested).toEqual(['2026-11-11']);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('shows the card loading until its story opens', async () => {
    server.use(refreshSucceeds(), today());
    renderApp('/');
    const tapped = await screen.findByRole('link', { name: date });
    expect(tapped).toHaveAttribute('aria-busy', 'false');

    fireEvent.click(tapped);

    expect(tapped).toHaveAttribute('aria-busy', 'true');
    expect(await screen.findByRole('dialog')).toBeVisible();
    expect(tapped).toHaveAttribute('aria-busy', 'false');
  });

  it('asks for the date of the phone, not of UTC', async () => {
    vi.setSystemTime(new Date(2026, 10, 12, 0, 30));
    server.use(refreshSucceeds(), today());
    renderApp('/');

    expect(await screen.findByRole('link', { name: 'Thu 12 Nov' })).toBeVisible();
    expect(requested).toEqual(['2026-11-12']);
  });

  it('says so when today has no memories from earlier years', async () => {
    server.use(refreshSucceeds(), today([]));
    const { container } = renderApp('/');

    expect(await screen.findByText('Nothing from this day in earlier years')).toBeVisible();
    expect(screen.queryByRole('link', { name: date })).toBeNull();
    expect(todayBox()).toHaveAttribute('aria-busy', 'false');
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
        todayAnd([], (asked) => {
          requested.push(asked);
          return failing ? failure() : [yearGroup(2023, memory(ids.first2023, 2023))];
        }),
      ),
    );
    const { container } = renderApp('/');
    await waitFor(() => {
      expect(requested).toHaveLength(1);
    });

    await advance(6000);
    expect(requested).toHaveLength(3);
    expect(screen.queryByText(message)).toBeNull();
    expect(todayBox()).toHaveAttribute('aria-busy', 'true');
    await advance(2000);
    expect(requested).toHaveLength(4);
    expect(await screen.findByText(message)).toBeVisible();
    expect(todayBox()).toHaveAttribute('aria-busy', 'false');
    expect(await axeResults(container)).toHaveNoViolations();

    failing = false;
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('link', { name: date })).toBeVisible();
    expect(screen.queryByText(message)).toBeNull();
    expect(requested).toHaveLength(5);
  });

  it('moves to the new day when it becomes visible after midnight, not while hidden', async () => {
    vi.setSystemTime(new Date(2026, 11, 31, 23, 59));
    server.use(refreshSucceeds(), today());
    renderApp('/');
    await screen.findByRole('link', { name: 'Thu 31 Dec' });

    vi.setSystemTime(new Date(2027, 0, 1, 0, 1));
    act(() => {
      becomes('hidden');
    });
    await advance(0);
    expect(requested).toEqual(['2026-12-31']);

    act(() => {
      becomes('visible');
    });

    expect(await screen.findByRole('link', { name: 'Fri 1 Jan' })).toBeVisible();
    expect(requested.at(-1)).toBe('2027-01-01');
  });
});

describe("the card's turning", () => {
  let received: string[];

  beforeEach(() => {
    received = [];
    server.use(refreshSucceeds(), today(threeYears()), proxyAnswers(received));
  });

  async function openLanding() {
    const rendered = renderApp('/');
    await screen.findByRole('link', { name: date });
    return rendered;
  }

  const thumbnailsAsked = () => received.filter((url) => url.includes('/thumbnail?'));

  it("turns every 4 s to the next older year's first memory, wrapping round to the newest", async () => {
    const { container } = await openLanding();
    expect(onScreen()).toBe(thumbnail(ids.first2025));

    await advance(3999);
    expect(onScreen()).toBe(thumbnail(ids.first2025));
    await advance(1);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2021));
    await advance(4000);
    expect(onScreen()).toBe(thumbnail(ids.first2025));
    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
    expect(card()).toHaveAccessibleName(date);
    expect(card().querySelectorAll('[data-shown]')).toHaveLength(1);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it("waits for the next year's picture, and turns once it has arrived", async () => {
    const { handler, release } = holdProxy(thumbnail(ids.first2023), received);
    server.use(handler);
    await openLanding();

    await advance(10_000);
    expect(onScreen()).toBe(thumbnail(ids.first2025));

    release();
    await advance(0);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
    await advance(3999);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
    await advance(1);
    expect(onScreen()).toBe(fromQueue(ids.first2021));
  });

  it('keeps a picture that arrives early for its turn', async () => {
    await openLanding();
    await advance(2000);

    expect(onScreen()).toBe(thumbnail(ids.first2025));
    await advance(2000);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
  });

  it('leaves out a year whose picture fails', async () => {
    server.use(
      proxyAnswers(received, (url) =>
        url === thumbnail(ids.first2023) ? new Response(null, { status: 500 }) : undefined,
      ),
    );
    await openLanding();

    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2021));
    await advance(4000);
    expect(onScreen()).toBe(thumbnail(ids.first2025));
    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2021));
  });

  it('goes on to the year after one whose picture fails while it waits for it', async () => {
    let fail: () => void = () => undefined;
    const failing = new Promise<Response>((resolve) => {
      fail = () => {
        resolve(new Response(null, { status: 500 }));
      };
    });
    server.use(
      proxyAnswers(received, (url) => (url === thumbnail(ids.first2023) ? failing : undefined)),
    );
    await openLanding();
    await advance(4000);
    expect(onScreen()).toBe(thumbnail(ids.first2025));

    fail();
    await advance(0);

    expect(onScreen()).toBe(fromQueue(ids.first2021));
  });

  it('stands still on a day with one year', async () => {
    server.use(today([yearGroup(2025, memory(ids.first2025, 2025))]));
    await openLanding();

    await advance(12_000);

    expect(onScreen()).toBe(thumbnail(ids.first2025));
  });

  it('stands still on the newest year under reduced motion', async () => {
    reduceMotion();
    await openLanding();

    await advance(12_000);

    expect(onScreen()).toBe(thumbnail(ids.first2025));
  });

  it('stands still while the tab is hidden, and turns 4 s after it is visible again', async () => {
    await openLanding();
    await advance(2000);

    act(() => {
      becomes('hidden');
    });
    await advance(10_000);
    expect(onScreen()).toBe(thumbnail(ids.first2025));

    act(() => {
      becomes('visible');
    });
    await advance(3999);
    expect(onScreen()).toBe(thumbnail(ids.first2025));
    await advance(1);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
  });

  it('stands still while the card has keyboard focus, and turns 4 s after it leaves', async () => {
    await openLanding();
    const keys = user();
    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2023));

    await keys.tab();
    expect(card()).toHaveFocus();
    await advance(10_000);
    expect(onScreen()).toBe(fromQueue(ids.first2023));

    await keys.tab();
    expect(card()).not.toHaveFocus();
    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2021));
  });

  it('goes on turning while a pointer rests on it', async () => {
    await openLanding();

    await user().hover(card());
    await advance(4000);

    expect(onScreen()).toBe(fromQueue(ids.first2023));
  });

  it('opens the story at the newest year, whatever year is on screen', async () => {
    const { router } = await openLanding();
    await advance(4000);
    expect(onScreen()).toBe(fromQueue(ids.first2023));

    fireEvent.click(card());

    const story = await screen.findByRole('dialog');
    expect(within(story).getByRole('status')).toHaveTextContent('1 year ago · 2025');
    expect(router.state.location.search).toEqual({ year: 2025 });
  });

  it('stands still while a story is open, and goes on from the same year once it closes', async () => {
    await openLanding();
    await advance(4000);
    fireEvent.click(card());
    await screen.findByRole('dialog');

    await advance(4000);
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(onScreen()).toBe(fromQueue(ids.first2023));
    await advance(3999);
    expect(onScreen()).toBe(fromQueue(ids.first2023));
    await advance(1);
    expect(onScreen()).toBe(fromQueue(ids.first2021));
  });

  it("loads the other years' pictures newest first, only once the first screen has shown", async () => {
    const newest = holdPicture(thumbnail(ids.first2025));
    const tray = hold();
    server.use(
      recentDaysAnswers(async (window) => {
        await tray.until;
        return recentDaysOf(() => [])(window);
      }),
    );
    renderApp('/');
    await advance(1000);
    expect(received).toEqual([]);

    newest.load();
    await screen.findByRole('link', { name: date });
    await advance(1000);
    expect(received).toEqual([]);

    tray.release();
    await waitFor(() => {
      expect(received).toEqual([thumbnail(ids.first2023), thumbnail(ids.first2021)]);
    });
  });

  it('hands the queue its pictures again once a story closes, asking again only for one it had not finished', async () => {
    const { handler, release } = holdProxy(thumbnail(ids.first2021), received);
    server.use(handler);
    await openLanding();
    await advance(0);
    expect(thumbnailsAsked()).toEqual([thumbnail(ids.first2023), thumbnail(ids.first2021)]);

    fireEvent.click(card());
    await screen.findByRole('dialog');
    await advance(1000);
    const byStory = received.length;
    expect(thumbnailsAsked()).toEqual([thumbnail(ids.first2023), thumbnail(ids.first2021)]);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(1000);

    expect(received.slice(byStory)).toEqual([thumbnail(ids.first2021)]);
    release();
    await advance(10_000);
    expect(received.slice(byStory)).toEqual([thumbnail(ids.first2021)]);
  });
});

describe('a thumbnail that fails to load', () => {
  const renewal = miraSession.expiresIn * 800;
  let refreshes: number;
  let answeredAt: number;
  let holding: Promise<undefined> | undefined;

  beforeEach(() => {
    refreshes = 0;
    holding = undefined;
    server.use(
      refreshRespondsWith(async () => {
        refreshes++;
        await holding;
        answeredAt = Date.now();
        return HttpResponse.json(miraSession);
      }),
      today(),
      recentDaysAnswers(
        recentDaysOf((day) =>
          day === '2026-11-10' ? [yearGroup(2024, memory(ids.yesterday2024, 2024))] : [],
        ),
      ),
    );
  });

  const pictureLinks = () => [card(), screen.getByRole('link', { name: 'Yesterday' })];

  async function openLanding() {
    renderApp('/');
    await screen.findByRole('link', { name: 'Yesterday' });
    return screen.findByRole('link', { name: date });
  }

  function sleepUntil(milliseconds: number) {
    vi.setSystemTime(answeredAt + milliseconds);
  }

  async function fails(image: HTMLImageElement) {
    fireEvent.error(image);
    await advance(0);
  }

  describe('before the session is due to renew', () => {
    it('loads again once, without refreshing', async () => {
      const link = await openLanding();
      sleepUntil(renewal);
      const first = pictureIn(link) as HTMLImageElement;

      await fails(first);
      const second = pictureIn(link) as HTMLImageElement;
      expect(refreshes).toBe(1);
      expect(second).not.toBe(first);
      expect(second.getAttribute('src')).toBe(thumbnail(ids.first2025));

      await fails(second);
      expect(refreshes).toBe(1);
      expect(pictureIn(link)).toBe(second);
    });

    it('sends no refresh for several failing one after another', async () => {
      await openLanding();
      const links = pictureLinks();
      const firsts = links.map((link) => pictureIn(link) as HTMLImageElement);

      for (const image of firsts) await fails(image);

      expect(refreshes).toBe(1);
      links.forEach((link, i) => {
        expect(pictureIn(link)).not.toBe(firsts[i]);
      });
    });

    it('waits for a refresh already on its way, and loads again once it answers', async () => {
      const link = await openLanding();
      let answer: () => void = () => undefined;
      holding = new Promise((resolve) => {
        answer = () => {
          resolve(undefined);
        };
      });
      await advance(answeredAt + renewal - Date.now());
      expect(refreshes).toBe(2);
      const first = pictureIn(link) as HTMLImageElement;

      await fails(first);
      expect(pictureIn(link)).toBe(first);

      answer();
      await advance(0);
      expect(refreshes).toBe(2);
      expect(pictureIn(link)).not.toBe(first);
    });
  });

  describe('once the session is due to renew', () => {
    it('renews the session once and loads again once', async () => {
      const link = await openLanding();
      sleepUntil(renewal + 1);
      const first = pictureIn(link) as HTMLImageElement;

      await fails(first);
      const second = pictureIn(link) as HTMLImageElement;
      expect(refreshes).toBe(2);
      expect(second).not.toBe(first);
      expect(second.getAttribute('src')).toBe(thumbnail(ids.first2025));

      sleepUntil(renewal + 1);
      await fails(second);
      expect(refreshes).toBe(2);
      expect(pictureIn(link)).toBe(second);
    });

    it('shares one renewal with the others failing at the same time', async () => {
      await openLanding();
      sleepUntil(renewal + 1);

      const links = pictureLinks();
      const firsts = links.map((link) => pictureIn(link) as HTMLImageElement);
      for (const image of firsts) fireEvent.error(image);
      await advance(0);

      expect(refreshes).toBe(2);
      links.forEach((link, i) => {
        expect(pictureIn(link)).not.toBe(firsts[i]);
      });
    });
  });
});
