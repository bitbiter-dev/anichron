import { fireEvent, screen, within, waitFor } from '@testing-library/react';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { problemResponse, refreshSucceeds } from '../../test/handlers/auth';
import {
  hold,
  memory,
  onThisDayAnswers,
  proxyUrl,
  recentDaysAnswers,
  recentDaysOf,
  type RecentWindow,
  yearGroup,
} from '../../test/handlers/flashbacks';
import { keptSource } from '../../test/object-urls';
import { holdPicture } from '../../test/pictures';
import { proxyAnswers } from '../../test/handlers/proxies';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { becomes } from '../../test/visibility';
import { advance } from '../../test/time';

const id = (date: string, n = 0) =>
  `0199a1b2-c3d4-7e5f-8a9b-${String(n)}000${date.replaceAll('-', '')}`;
const thumbnail = (assetId: string) => proxyUrl(assetId, 'thumbnail');

let requested: RecentWindow[];
let dayRequests: string[];
let days: Record<string, ReturnType<typeof yearGroup>[]>;
let answer: (window: RecentWindow) => Response | null | Promise<Response | null>;

const onDay = (date: string, year: number, seed = 0) =>
  yearGroup(year, memory(id(date, seed), year), memory(id(date, seed + 1), year));

function onEachDay(month: string, first: number, last: number, year: number) {
  for (let day = first; day <= last; day++) {
    const date = `${month}${String(day).padStart(2, '0')}`;
    days[date] = [onDay(date, year)];
  }
}

const tray = () => screen.getByRole('list', { name: 'Recent days' });
const circles = () => within(tray()).queryAllByRole('link');
const items = () =>
  within(tray())
    .getAllByRole('listitem')
    .map((item) => item.textContent);
const pictureIn = (element: HTMLElement) => element.querySelector('img');
const firstPage = { before: '2026-11-11', since: '2026-11-05', limit: 6 };
const later = (limit: number) => ({ before: '2026-11-05', since: null, limit });
const together = (...windows: RecentWindow[]) => {
  expect(requested.toSorted((a, b) => a.before.localeCompare(b.before))).toEqual(
    windows.toSorted((a, b) => a.before.localeCompare(b.before)),
  );
};

function holdPreview(assetId: string) {
  const url = proxyUrl(assetId, 'preview');
  let answer: (value: undefined) => void = () => undefined;
  server.use(
    proxyAnswers([], (asked) =>
      asked === url
        ? new Promise<undefined>((resolve) => {
            answer = resolve;
          })
        : undefined,
    ),
  );
  return {
    load: () => {
      answer(undefined);
    },
  };
}

beforeEach(() => {
  requested = [];
  dayRequests = [];
  days = {};
  answer = () => null;
  vi.setSystemTime(new Date(2026, 10, 11, 8));
  server.use(
    refreshSucceeds(),
    onThisDayAnswers((date) => {
      dayRequests.push(date);
      return days[date] ?? [];
    }),
    recentDaysAnswers(async (window) => {
      requested.push(window);
      return (await answer(window)) ?? recentDaysOf((date) => days[date] ?? [])(window);
    }),
  );
});

async function shownTray() {
  return within(await screen.findByRole('list', { name: 'Recent days' }));
}

describe('the recent days', () => {
  it('shows those of the last six days, newest first, each with its first memory, from one request', async () => {
    days = {
      '2026-11-11': [onDay('2026-11-11', 2025)],
      '2026-11-10': [onDay('2026-11-10', 2024), onDay('2026-11-10', 2020, 2)],
      '2026-11-08': [onDay('2026-11-08', 2022)],
      '2026-11-05': [onDay('2026-11-05', 2019)],
      '2026-11-04': [onDay('2026-11-04', 2019)],
    };
    const { container } = renderApp('/');

    expect(await (await shownTray()).findByRole('link', { name: 'Thu 5 Nov' })).toBeVisible();
    expect(circles().map((circle) => circle.textContent)).toEqual([
      'Yesterday',
      'Sun 8 Nov',
      'Thu 5 Nov',
    ]);
    expect(circles().map((circle) => pictureIn(circle)?.getAttribute('src'))).toEqual([
      thumbnail(id('2026-11-10')),
      thumbnail(id('2026-11-08')),
      thumbnail(id('2026-11-05')),
    ]);
    expect(requested).toEqual([firstPage]);
    expect(dayRequests).toEqual(['2026-11-11']);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each(['load', 'fail'] as const)(
    'shows the days only once the last of their pictures %ss',
    async (outcome) => {
      const thumbnailHeld = holdPicture(thumbnail(id('2026-11-08')));
      days = {
        '2026-11-10': [onDay('2026-11-10', 2024)],
        '2026-11-08': [onDay('2026-11-08', 2022)],
      };
      renderApp('/');
      await shownTray();
      await advance(1000);

      expect(circles()).toEqual([]);
      expect(tray()).toHaveAttribute('aria-busy', 'true');
      thumbnailHeld[outcome]();
      expect(await within(tray()).findByRole('link', { name: 'Sun 8 Nov' })).toBeVisible();
      expect(circles().map((circle) => circle.textContent)).toEqual(['Yesterday', 'Sun 8 Nov']);
      expect(tray()).toHaveAttribute('aria-busy', 'false');
    },
  );

  it('marks the tray busy, showing none of it, while its first days load', async () => {
    const held = hold();
    days['2026-11-09'] = [onDay('2026-11-09', 2021)];
    answer = async () => {
      await held.until;
      return Response.json({ days: recentDaysOf((date) => days[date] ?? [])(firstPage) });
    };
    const { container } = renderApp('/');
    await vi.waitFor(() => {
      expect(requested).toHaveLength(1);
    });
    await advance(1000);

    expect(tray()).toHaveAttribute('aria-busy', 'true');
    expect(within(tray()).queryAllByRole('listitem')).toEqual([]);
    expect(screen.queryByRole('button', { name: 'Earlier days' })).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();

    held.release();

    expect(await within(tray()).findByRole('link', { name: 'Mon 9 Nov' })).toBeVisible();
    expect(tray()).toHaveAttribute('aria-busy', 'false');
  });

  it('shows one Try again in place of the days when they fail, and Try again loads them', async () => {
    days['2026-11-09'] = [onDay('2026-11-09', 2021)];
    answer = () => problemResponse(500, 'internal_error');
    const { container } = renderApp('/');
    await shownTray();

    await advance(8000);
    expect(items()).toEqual(["Try againCouldn't load", 'Earlier days']);
    expect(await axeResults(container)).toHaveNoViolations();

    answer = () => null;
    fireEvent.click(within(tray()).getByRole('button', { name: 'Try loading these days again' }));

    expect(await within(tray()).findByRole('link', { name: 'Mon 9 Nov' })).toBeVisible();
    expect(within(tray()).queryByRole('button', { name: /Try/ })).toBeNull();
  });

  it('says Nothing in recent days next to Earlier days when the last six days have none', async () => {
    const { container } = renderApp('/');

    expect(await (await shownTray()).findByText('Nothing in recent days')).toBeVisible();
    expect(items()).toEqual(['Nothing in recent days', 'Earlier days']);
    expect(tray()).toHaveAttribute('aria-busy', 'false');
    expect(screen.getByText('Nothing from this day in earlier years')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('drops the message once an earlier day shows', async () => {
    days['2026-09-01'] = [onDay('2026-09-01', 2020)];
    renderApp('/');
    await (await shownTray()).findByText('Nothing in recent days');

    fireEvent.click(within(tray()).getByRole('button', { name: 'Earlier days' }));

    expect(await within(tray()).findByRole('link', { name: 'Tue 1 Sep' })).toBeVisible();
    expect(items()).toEqual(['Tue 1 Sep', 'Nothing earlier']);
  });

  it.each([
    ['has memories', () => [onDay('2026-11-11', 2025)]],
    ['has none', () => []],
    ['failed', () => problemResponse(500, 'internal_error')],
  ])('shows the days whatever today turned out: today %s', async (_, today) => {
    days['2026-11-10'] = [onDay('2026-11-10', 2024)];
    server.use(onThisDayAnswers((date) => (date === '2026-11-11' ? today() : (days[date] ?? []))));
    renderApp('/');

    expect(await (await shownTray()).findByRole('link', { name: 'Yesterday' })).toBeVisible();
    expect(items()).toEqual(['Yesterday', 'Earlier days']);
  });

  it('shows the days without waiting for today', async () => {
    const todayHeld = hold();
    days['2026-11-10'] = [onDay('2026-11-10', 2024)];
    server.use(
      onThisDayAnswers(async (date) => {
        if (date === '2026-11-11') await todayHeld.until;
        return days[date] ?? [];
      }),
    );
    renderApp('/');

    expect(await (await shownTray()).findByRole('link', { name: 'Yesterday' })).toBeVisible();
    expect(tray()).toHaveAttribute('aria-busy', 'false');
    todayHeld.release();
  });

  it("opens a day's story at its newest year group, starting on the day's first memory", async () => {
    vi.setSystemTime(new Date(2027, 0, 1, 8));
    days = { '2026-12-31': [onDay('2026-12-31', 2024), onDay('2026-12-31', 2020, 2)] };
    const { router } = renderApp('/');

    fireEvent.click(await (await shownTray()).findByRole('link', { name: 'Yesterday' }));

    const story = await screen.findByRole('dialog', { name: 'Memories of December 31, 2026' });
    expect(router.state.location.href).toBe('/day/2026-12-31?year=2024');
    expect(within(story).getByText('2 years ago · 2024')).toBeVisible();
    expect(story.querySelector('img')?.getAttribute('src')).toBe(
      keptSource(proxyUrl(id('2026-12-31'), 'preview')),
    );
  });

  it('shows a tapped day loading until its first picture is loaded, then opens on it', async () => {
    days = { '2026-11-10': [onDay('2026-11-10', 2024)], '2026-11-09': [onDay('2026-11-09', 2023)] };
    renderApp('/');
    const tapped = await (await shownTray()).findByRole('link', { name: 'Yesterday' });
    expect(tapped).toHaveAttribute('aria-busy', 'false');
    const pictureHeld = holdPreview(id('2026-11-10'));

    fireEvent.click(tapped);
    await advance(1000);

    expect(tapped).toHaveAttribute('aria-busy', 'true');
    expect(within(tray()).getByRole('link', { name: 'Mon 9 Nov' })).toHaveAttribute(
      'aria-busy',
      'false',
    );
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(dayRequests).toEqual(['2026-11-11']);
    pictureHeld.load();
    expect(await screen.findByRole('dialog')).toBeVisible();
    expect(tapped).toHaveAttribute('aria-busy', 'false');
  });

  it('shows an earlier day loading until its first picture is loaded', async () => {
    days = { '2026-11-10': [onDay('2026-11-10', 2024)], '2026-10-31': [onDay('2026-10-31', 2023)] };
    renderApp('/');
    fireEvent.click(await (await shownTray()).findByRole('button', { name: /Earlier days/ }));
    const tapped = await within(tray()).findByRole('link', { name: 'Sat 31 Oct' });
    const pictureHeld = holdPreview(id('2026-10-31'));

    fireEvent.click(tapped);
    await advance(1000);

    expect(tapped).toHaveAttribute('aria-busy', 'true');
    pictureHeld.load();
    expect(await screen.findByRole('dialog')).toBeVisible();
    expect(tapped).toHaveAttribute('aria-busy', 'false');
  });

  it('reads them again once a story opened from them closes', async () => {
    days = { '2026-11-10': [onDay('2026-11-10', 2024)], '2026-11-09': [onDay('2026-11-09', 2023)] };
    renderApp('/');
    fireEvent.click(await (await shownTray()).findByRole('link', { name: 'Yesterday' }));
    const story = await screen.findByRole('dialog');
    delete days['2026-11-10'];

    fireEvent.click(within(story).getByRole('button', { name: 'Close' }));

    await vi.waitFor(() => {
      expect(circles().map((circle) => circle.textContent)).toEqual(['Mon 9 Nov']);
    });
    expect(requested).toEqual([firstPage, firstPage]);
  });
});

describe('earlier days', () => {
  const earlier = () => within(tray()).getByRole('button', { name: /Earlier days|Nothing/ });

  async function openLanding() {
    days['2026-11-10'] = [onDay('2026-11-10', 2024)];
    const rendered = renderApp('/');
    await (await shownTray()).findByRole('link', { name: 'Yesterday' });
    return rendered;
  }

  it('is the last item of the tray, and adds the next recent days however far back, from one request', async () => {
    days['2026-11-02'] = [onDay('2026-11-02', 2023)];
    days['2026-09-01'] = [onDay('2026-09-01', 2020)];
    const { container } = await openLanding();
    expect(within(tray()).getAllByRole('listitem').at(-1)).toContainElement(earlier());

    fireEvent.click(earlier());

    expect(await within(tray()).findByRole('link', { name: 'Tue 1 Sep' })).toBeVisible();
    expect(requested).toEqual([firstPage, later(6)]);
    expect(items()).toEqual(['Yesterday', 'Mon 2 Nov', 'Tue 1 Sep', 'Nothing earlier']);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('adds the next recent days with a tap from an address with no earlier pages', async () => {
    days['2026-11-10'] = [onDay('2026-11-10', 2024)];
    days['2026-11-02'] = [onDay('2026-11-02', 2023)];
    renderApp('/?earlier=0');
    await (await shownTray()).findByRole('link', { name: 'Yesterday' });

    fireEvent.click(earlier());

    expect(await within(tray()).findByRole('link', { name: 'Mon 2 Nov' })).toBeVisible();
    expect(requested).toEqual([firstPage, later(6)]);
  });

  it('asks for six more recent days with each tap', async () => {
    onEachDay('2026-10-', 1, 13, 2020);
    await openLanding();

    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Thu 8 Oct' });
    fireEvent.click(earlier());

    expect(await within(tray()).findByRole('link', { name: 'Fri 2 Oct' })).toBeVisible();
    expect(requested).toEqual([firstPage, later(6), later(12)]);
    expect(circles()).toHaveLength(13);
    expect(earlier()).toHaveAccessibleName('Earlier days');
  });

  it('becomes a disabled Nothing earlier that stays when a page comes back short', async () => {
    days['2026-10-18'] = [onDay('2026-10-18', 2023)];
    const { container } = await openLanding();

    fireEvent.click(earlier());

    expect(await within(tray()).findByRole('button', { name: 'Nothing earlier' })).toBe(earlier());
    expect(earlier()).toBeDisabled();
    expect(items()).toEqual(['Yesterday', 'Sun 18 Oct', 'Nothing earlier']);
    expect(await axeResults(container)).toHaveNoViolations();
    await advance(10_000);
    fireEvent.click(earlier());
    await advance(0);
    expect(earlier()).toHaveAccessibleName('Nothing earlier');
    expect(requested).toEqual([firstPage, later(6)]);
  });

  it('keeps Nothing earlier when a reread finds a day again', async () => {
    days['2026-10-18'] = [onDay('2026-10-18', 2023)];
    const { router } = await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('button', { name: 'Nothing earlier' });
    onEachDay('2026-09-', 1, 9, 2020);

    void router.options.context.queryClient.invalidateQueries({ queryKey: ['on-this-day'] });

    expect(await within(tray()).findByRole('link', { name: 'Sat 5 Sep' })).toBeVisible();
    expect(earlier()).toHaveAccessibleName('Nothing earlier');
  });

  it('shows it is loading until the days it added arrive', async () => {
    const held = hold();
    days['2026-10-31'] = [onDay('2026-10-31', 2023)];
    const { container } = await openLanding();
    expect(earlier()).not.toHaveAttribute('data-pending');
    answer = async (window) => {
      if (window.since !== null) return null;
      await held.until;
      return Response.json({ days: recentDaysOf((date) => days[date] ?? [])(window) });
    };

    fireEvent.click(earlier());

    await waitFor(() => {
      expect(requested).toHaveLength(2);
    });
    expect(earlier()).toHaveAttribute('data-pending', 'true');
    expect(earlier()).toHaveAttribute('aria-disabled', 'true');
    expect(await axeResults(container)).toHaveNoViolations();
    held.release();
    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(earlier()).not.toHaveAttribute('data-pending');
  });

  it('shows the days it had at once on coming back while the next ones load', async () => {
    onEachDay('2026-10-', 1, 13, 2020);
    const { router } = await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Thu 8 Oct' });
    const next = hold();
    answer = async (window) => {
      if (window.limit === 12) await next.until;
      return null;
    };
    fireEvent.click(earlier());

    await act(() => router.navigate({ to: '/settings' }));
    act(() => {
      router.history.back();
    });

    expect(await (await shownTray()).findByRole('link', { name: 'Thu 8 Oct' })).toBeVisible();
    expect(earlier()).toHaveAttribute('data-pending', 'true');
    next.release();
    expect(await within(tray()).findByRole('link', { name: 'Fri 2 Oct' })).toBeVisible();
  });

  it('keeps the days it added and shows one Try again after them when the next tap fails', async () => {
    onEachDay('2026-10-', 1, 9, 2020);
    const { container } = await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Sun 4 Oct' });
    answer = (window) => (window.limit === 12 ? problemResponse(500, 'internal_error') : null);

    fireEvent.click(earlier());
    await advance(8000);

    expect(items()).toEqual([
      'Yesterday',
      'Fri 9 Oct',
      'Thu 8 Oct',
      'Wed 7 Oct',
      'Tue 6 Oct',
      'Mon 5 Oct',
      'Sun 4 Oct',
      "Try againCouldn't load",
      'Earlier days',
    ]);
    expect(await axeResults(container)).toHaveNoViolations();

    answer = () => null;
    fireEvent.click(within(tray()).getByRole('button', { name: 'Try loading these days again' }));

    expect(await within(tray()).findByRole('link', { name: 'Thu 1 Oct' })).toBeVisible();
    expect(within(tray()).queryByRole('button', { name: /Try/ })).toBeNull();
  });

  it('never shows the days it added before the days above them', async () => {
    const thumbnailHeld = holdPicture(thumbnail(id('2026-11-10')));
    days = {
      '2026-11-10': [onDay('2026-11-10', 2024)],
      '2026-10-31': [onDay('2026-10-31', 2023)],
    };
    renderApp('/?earlier=2');
    await vi.waitFor(() => {
      together(firstPage, later(6));
    });
    await advance(1000);

    expect(circles()).toEqual([]);
    expect(tray()).toHaveAttribute('aria-busy', 'true');
    thumbnailHeld.load();
    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(circles().map((circle) => circle.textContent)).toEqual(['Yesterday', 'Sat 31 Oct']);
  });

  it('keeps the days shown while it adds more whose pictures still load', async () => {
    const thumbnailHeld = holdPicture(thumbnail(id('2026-10-31')));
    days['2026-10-31'] = [onDay('2026-10-31', 2023)];
    await openLanding();

    const button = earlier();
    fireEvent.click(button);
    await advance(1000);

    expect(circles().map((circle) => circle.textContent)).toEqual(['Yesterday']);
    expect(button).toHaveAttribute('data-pending', 'true');
    thumbnailHeld.load();
    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(button).not.toHaveAttribute('data-pending');
  });

  it('shows the next six without waiting again for pictures of the six above it', async () => {
    onEachDay('2026-10-', 1, 13, 2020);
    await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Thu 8 Oct' });
    const thumbnailHeld = holdPicture(thumbnail(id('2026-10-02')));

    fireEvent.click(earlier());
    await advance(1000);

    expect(circles()).toHaveLength(7);
    thumbnailHeld.load();
    expect(await within(tray()).findByRole('link', { name: 'Fri 2 Oct' })).toBeVisible();
  });

  describe('once it added more', () => {
    const bar = () => screen.getByRole('navigation', { name: '' });

    async function addedEarlier() {
      onEachDay('2026-10-', 21, 29, 2023);
      const rendered = await openLanding();
      fireEvent.click(earlier());
      await within(tray()).findByRole('link', { name: 'Sat 24 Oct' });
      return rendered;
    }

    const shownAgain = async () => {
      const shown = await shownTray();
      expect(await shown.findByRole('link', { name: 'Sat 24 Oct' })).toBeVisible();
      expect(circles()).toHaveLength(7);
      expect(tray()).toHaveAttribute('aria-busy', 'false');
    };

    it('keeps them in the address, without a new entry in history', async () => {
      const { router } = await addedEarlier();

      expect(router.state.location.href).toBe('/?earlier=2');
      expect(router.history.length).toBe(1);
    });

    it('opens with them from the address, one request for each page and one for the rest', async () => {
      onEachDay('2026-10-', 21, 29, 2023);
      days['2026-11-10'] = [onDay('2026-11-10', 2024)];
      renderApp('/?earlier=2');

      await shownAgain();
      together(firstPage, later(6));
    });

    it.each([
      [
        'Today in the bottom bar',
        () => fireEvent.click(within(bar()).getByRole('link', { name: 'Today' })),
      ],
      [
        'Back',
        (router: ReturnType<typeof renderApp>['router']) => {
          router.history.back();
        },
      ],
    ])('still shows them, from what it read, back from Settings through %s', async (_, back) => {
      const { router } = await addedEarlier();
      fireEvent.click(within(bar()).getByRole('link', { name: 'Settings' }));
      await screen.findByRole('heading', { level: 1, name: 'Settings' });
      const answers = hold();
      answer = async () => {
        await answers.until;
        return Response.json({ days: [] });
      };

      act(() => {
        back(router);
      });

      await shownAgain();
      expect(router.state.location.href).toBe('/?earlier=2');
      answers.release();
    });

    it('still shows them behind a story opened from them, and once it closes', async () => {
      const { router } = await addedEarlier();

      fireEvent.click(within(tray()).getByRole('link', { name: 'Sat 24 Oct' }));
      const story = await screen.findByRole('dialog');
      expect(router.state.location.href).toBe('/day/2026-10-24?year=2023&earlier=2');
      fireEvent.click(within(story).getByRole('button', { name: 'Close' }));

      await vi.waitFor(() => {
        expect(screen.queryByRole('dialog')).toBeNull();
      });
      await shownAgain();
      expect(router.state.location.href).toBe('/?earlier=2');
    });

    it.each([
      ['0', null],
      ['-1', null],
      ['soon', null],
      ['2.5', 6],
      ['Infinity', 360],
      ['100000', 360],
    ])('reads an address of earlier=%s as asking for %s more', async (earlierPages, limit) => {
      days['2026-11-10'] = [onDay('2026-11-10', 2024)];
      renderApp(`/?earlier=${earlierPages}`);
      await (await shownTray()).findByRole('link', { name: 'Yesterday' });
      await advance(0);

      if (limit === null) together(firstPage);
      else together(firstPage, later(limit));
    });

    it('keeps them when it becomes visible on the same day', async () => {
      const { router } = await addedEarlier();

      act(() => {
        becomes('hidden');
      });
      act(() => {
        becomes('visible');
      });
      await advance(0);

      await shownAgain();
      expect(router.state.location.href).toBe('/?earlier=2');
    });
  });

  it('starts again from the last six days when it becomes visible on a new day', async () => {
    days['2026-10-31'] = [onDay('2026-10-31', 2023)];
    const { router } = await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Sat 31 Oct' });

    vi.setSystemTime(new Date(2026, 10, 12, 8));
    act(() => {
      becomes('hidden');
    });
    await advance(1000);
    expect(router.state.location.href).toBe('/?earlier=2');
    act(() => {
      becomes('visible');
    });
    await advance(0);

    await vi.waitFor(() => {
      expect(within(tray()).getByRole('link', { name: 'Tue 10 Nov' })).toBeVisible();
    });
    expect(items()).toEqual(['Tue 10 Nov', 'Earlier days']);
    expect(requested.at(-1)).toEqual({ before: '2026-11-12', since: '2026-11-06', limit: 6 });
    expect(router.state.location.href).toBe('/');
    expect(router.history.length).toBe(1);
  });
});
