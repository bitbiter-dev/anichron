import { fireEvent, screen, within } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { problemResponse, refreshSucceeds } from '../../test/handlers/auth';
import { hold, memory, onThisDayAnswers, yearGroup } from '../../test/handlers/flashbacks';
import { proxyAnswers } from '../../test/handlers/proxies';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { becomes } from '../../test/visibility';

type Years = ReturnType<typeof yearGroup>[] | (() => Response | Promise<Response>);

const id = (date: string, n = 0) =>
  `0199a1b2-c3d4-7e5f-8a9b-${String(n)}000${date.replaceAll('-', '')}`;
const thumbnail = (assetId: string) => `/api/v1/proxies/${assetId}/thumbnail?v=0123456789abcdef`;

let requested: string[];
let days: Record<string, Years>;

const onDay = (date: string, year: number, seed = 0) =>
  yearGroup(year, memory(id(date, seed), year), memory(id(date, seed + 1), year));

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

const tray = () => screen.getByRole('list', { name: 'Recent days' });
const circles = () => within(tray()).queryAllByRole('link');
const pictureIn = (element: HTMLElement) => element.querySelector('img');

beforeEach(() => {
  requested = [];
  days = {};
  vi.setSystemTime(new Date(2026, 10, 11, 8));
  server.use(
    refreshSucceeds(),
    onThisDayAnswers((date) => {
      requested.push(date);
      const day = days[date] ?? [];
      return typeof day === 'function' ? day() : day;
    }),
  );
});

describe('the recent days', () => {
  it('shows the last six days with memories, newest first, each with its first memory', async () => {
    days = {
      '2026-11-11': [onDay('2026-11-11', 2025)],
      '2026-11-10': [onDay('2026-11-10', 2024), onDay('2026-11-10', 2020, 2)],
      '2026-11-08': [onDay('2026-11-08', 2022)],
      '2026-11-05': [onDay('2026-11-05', 2019)],
    };
    const { container } = renderApp('/');

    expect(
      await within(await screen.findByRole('list', { name: 'Recent days' })).findByRole('link', {
        name: 'Thu 5 Nov',
      }),
    ).toBeVisible();
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
    expect(requested.toSorted()).toEqual([
      '2026-11-05',
      '2026-11-06',
      '2026-11-07',
      '2026-11-08',
      '2026-11-09',
      '2026-11-10',
      '2026-11-11',
    ]);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each([
    ['loads', () => undefined],
    ['fails', () => new HttpResponse(null, { status: 503 })],
  ])('shows the six days only once the last of their pictures %s', async (_, outcome) => {
    const thumbnailHeld = hold();
    days = {
      '2026-11-11': [onDay('2026-11-11', 2025)],
      '2026-11-10': [onDay('2026-11-10', 2024)],
      '2026-11-08': [onDay('2026-11-08', 2022)],
    };
    server.use(
      proxyAnswers([], async (url) => {
        if (url !== thumbnail(id('2026-11-08'))) return undefined;
        await thumbnailHeld.until;
        return outcome();
      }),
    );
    renderApp('/');
    await screen.findByRole('list', { name: 'Recent days' });
    await advance(1000);

    expect(circles()).toEqual([]);
    thumbnailHeld.release();
    expect(await within(tray()).findByRole('link', { name: 'Sun 8 Nov' })).toBeVisible();
    expect(circles().map((circle) => circle.textContent)).toEqual(['Yesterday', 'Sun 8 Nov']);
  });

  it('never shows six days it added before the six before them', async () => {
    const thumbnailHeld = hold();
    days = {
      '2026-11-11': [onDay('2026-11-11', 2025)],
      '2026-11-10': [onDay('2026-11-10', 2024)],
      '2026-10-31': [onDay('2026-10-31', 2023)],
    };
    server.use(
      proxyAnswers([], async (url) => {
        if (url === thumbnail(id('2026-11-10'))) await thumbnailHeld.until;
        return undefined;
      }),
    );
    renderApp('/');
    fireEvent.click(
      await within(await screen.findByRole('list', { name: 'Recent days' })).findByRole('button', {
        name: 'Earlier days',
      }),
    );
    await advance(1000);

    expect(circles()).toEqual([]);
    thumbnailHeld.release();
    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(circles().map((circle) => circle.textContent)).toEqual(['Yesterday', 'Sat 31 Oct']);
  });

  it('keeps the days shown while it adds six more whose pictures still load', async () => {
    const thumbnailHeld = hold();
    days = {
      '2026-11-10': [onDay('2026-11-10', 2024)],
      '2026-10-31': [onDay('2026-10-31', 2023)],
    };
    server.use(
      proxyAnswers([], async (url) => {
        if (url !== thumbnail(id('2026-10-31'))) return undefined;
        await thumbnailHeld.until;
        return undefined;
      }),
    );
    renderApp('/');
    await within(await screen.findByRole('list', { name: 'Recent days' })).findByRole('link', {
      name: 'Yesterday',
    });

    const earlier = within(tray()).getByRole('button', { name: 'Earlier days' });
    fireEvent.click(earlier);
    await advance(1000);

    expect(circles().map((circle) => circle.textContent)).toEqual(['Yesterday']);
    expect(earlier).toHaveAttribute('data-pending', 'true');
    thumbnailHeld.release();
    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(earlier).not.toHaveAttribute('data-pending');
  });

  it('shows Try again in the place of a day that failed, and Try again loads it', async () => {
    days = {
      '2026-11-10': [onDay('2026-11-10', 2024)],
      '2026-11-09': () => problemResponse(500, 'internal_error'),
      '2026-11-08': [onDay('2026-11-08', 2022)],
    };
    const { container } = renderApp('/');
    await screen.findByRole('list', { name: 'Recent days' });

    await advance(8000);
    const items = within(tray()).getAllByRole('listitem');
    expect(items.map((item) => item.textContent)).toEqual([
      'Yesterday',
      'Try againMon 9 Nov',
      'Sun 8 Nov',
      'Earlier days',
    ]);
    expect(await axeResults(container)).toHaveNoViolations();

    days['2026-11-09'] = [onDay('2026-11-09', 2021)];
    fireEvent.click(within(tray()).getByRole('button', { name: 'Try again, Mon 9 Nov' }));

    expect(await within(tray()).findByRole('link', { name: 'Mon 9 Nov' })).toBeVisible();
    expect(within(tray()).queryByRole('button', { name: /Try again/ })).toBeNull();
  });

  it("opens a day's story at its newest year group, counted from that day's year", async () => {
    vi.setSystemTime(new Date(2027, 0, 1, 8));
    days = { '2026-12-31': [onDay('2026-12-31', 2024), onDay('2026-12-31', 2020, 2)] };
    const { router } = renderApp('/');

    fireEvent.click(
      await within(await screen.findByRole('list', { name: 'Recent days' })).findByRole('link', {
        name: 'Yesterday',
      }),
    );

    const story = await screen.findByRole('dialog', { name: 'Memories of December 31, 2026' });
    expect(router.state.location.href).toBe('/day/2026-12-31?year=2024');
    expect(within(story).getByText('2 years ago · 2024')).toBeVisible();
    expect(story.querySelector('img')?.getAttribute('src')).toBe(
      `/api/v1/proxies/${id('2026-12-31')}/preview?v=0123456789abcdef`,
    );
  });

  it('shows a tapped day loading until its story opens', async () => {
    days = { '2026-11-10': [onDay('2026-11-10', 2024)], '2026-11-09': [onDay('2026-11-09', 2023)] };
    renderApp('/');
    const tapped = await within(
      await screen.findByRole('list', { name: 'Recent days' }),
    ).findByRole('link', { name: 'Yesterday' });
    expect(tapped).toHaveAttribute('aria-busy', 'false');

    fireEvent.click(tapped);

    expect(tapped).toHaveAttribute('aria-busy', 'true');
    expect(within(tray()).getByRole('link', { name: 'Mon 9 Nov' })).toHaveAttribute(
      'aria-busy',
      'false',
    );
    expect(await screen.findByRole('dialog')).toBeVisible();
    expect(tapped).toHaveAttribute('aria-busy', 'false');
  });

  it('shows only the message when the whole week is empty', async () => {
    const { container } = renderApp('/');

    expect(await screen.findByText('Nothing from this day in earlier years')).toBeVisible();
    await vi.waitFor(() => {
      expect(requested).toHaveLength(7);
    });
    await advance(0);
    expect(screen.queryByRole('list', { name: 'Recent days' })).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('shows the recent days below the message when only today is empty', async () => {
    days = { '2026-11-07': [onDay('2026-11-07', 2022)] };
    renderApp('/');

    expect(
      await within(await screen.findByRole('list', { name: 'Recent days' })).findByRole('link', {
        name: 'Sat 7 Nov',
      }),
    ).toBeVisible();
    expect(screen.getByText('Nothing from this day in earlier years')).toBeVisible();
  });

  it('shows only Earlier days in the tray when today has memories and the six days none', async () => {
    days = { '2026-11-11': [onDay('2026-11-11', 2025)] };
    renderApp('/');

    await screen.findByRole('link', { name: '1 year ago · 2025' });
    await advance(0);
    expect(
      within(tray())
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Earlier days']);
  });
});

describe('earlier days', () => {
  const earlier = () => within(tray()).getByRole('button', { name: /Earlier days|Nothing/ });
  const newRequests = (since: number) => requested.slice(since).toSorted();

  async function openLanding() {
    days['2026-11-10'] = [onDay('2026-11-10', 2024)];
    const rendered = renderApp('/');
    await within(await screen.findByRole('list', { name: 'Recent days' })).findByRole('link', {
      name: 'Yesterday',
    });
    return rendered;
  }

  it('is the last item of the tray, and adds the next six calendar days with memories', async () => {
    days['2026-10-31'] = [onDay('2026-10-31', 2023)];
    const { container } = await openLanding();
    expect(within(tray()).getAllByRole('listitem').at(-1)).toContainElement(earlier());
    const before = requested.length;

    fireEvent.click(earlier());

    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(newRequests(before)).toEqual([
      '2026-10-30',
      '2026-10-31',
      '2026-11-01',
      '2026-11-02',
      '2026-11-03',
      '2026-11-04',
    ]);
    expect(
      within(tray())
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Yesterday', 'Sat 31 Oct', 'Earlier days']);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('says Nothing in these days briefly when it adds none, and keeps it while the next tap loads', async () => {
    days['2026-10-18'] = [onDay('2026-10-18', 2023)];
    const { container } = await openLanding();

    fireEvent.click(earlier());
    expect(await within(tray()).findByRole('button', { name: 'Nothing in these days' })).toBe(
      earlier(),
    );
    expect(await axeResults(container)).toHaveNoViolations();
    await advance(1900);
    expect(earlier()).toHaveAccessibleName('Nothing in these days');
    await advance(100);
    expect(earlier()).toHaveAccessibleName('Earlier days');

    fireEvent.click(earlier());
    expect(await within(tray()).findByRole('button', { name: 'Nothing in these days' })).toBe(
      earlier(),
    );
    const before = requested.length;

    fireEvent.click(earlier());

    expect(earlier()).toHaveAccessibleName('Nothing in these days');
    expect(await within(tray()).findByRole('link', { name: 'Sun 18 Oct' })).toBeVisible();
    expect(newRequests(before)).toEqual([
      '2026-10-18',
      '2026-10-19',
      '2026-10-20',
      '2026-10-21',
      '2026-10-22',
      '2026-10-23',
    ]);
    expect(earlier()).toHaveAccessibleName('Earlier days');
  });

  it('shows it is loading until the days it added arrive', async () => {
    const dayHeld = hold();
    days['2026-10-31'] = async () => {
      await dayHeld.until;
      return HttpResponse.json({ date: '2026-10-31', years: [onDay('2026-10-31', 2023)] });
    };
    const { container } = await openLanding();
    expect(earlier()).not.toHaveAttribute('data-pending');

    fireEvent.click(earlier());

    await vi.waitFor(() => {
      expect(requested).toContain('2026-10-31');
    });
    expect(earlier()).toHaveAttribute('data-pending', 'true');
    expect(earlier()).toHaveAttribute('aria-disabled', 'true');
    expect(await axeResults(container)).toHaveNoViolations();
    dayHeld.release();
    expect(await within(tray()).findByRole('link', { name: 'Sat 31 Oct' })).toBeVisible();
    expect(earlier()).not.toHaveAttribute('data-pending');
  });

  it('holds the first six days back, saying Earlier days, until the last of them arrives', async () => {
    days['2026-11-11'] = [onDay('2026-11-11', 2025)];
    days['2026-11-10'] = [onDay('2026-11-10', 2024)];
    days['2026-11-09'] = () =>
      new Promise((resolve) =>
        setTimeout(() => {
          resolve(Response.json({ date: '2026-11-09', years: [] }));
        }, 3000),
      );
    renderApp('/');
    await screen.findByRole('list', { name: 'Recent days' });
    await advance(2900);

    expect(circles()).toEqual([]);
    expect(earlier()).toHaveAccessibleName('Earlier days');
    await advance(100);
    expect(await within(tray()).findByRole('link', { name: 'Yesterday' })).toBeVisible();
  });

  it('says nothing when a retried day of the first six turns out empty', async () => {
    days['2026-11-09'] = () => problemResponse(500, 'internal_error');
    await openLanding();
    await advance(8000);

    days['2026-11-09'] = [];
    fireEvent.click(within(tray()).getByRole('button', { name: 'Try again, Mon 9 Nov' }));

    await vi.waitFor(() => {
      expect(within(tray()).queryByRole('button', { name: /Try again/ })).toBeNull();
    });
    expect(earlier()).toHaveAccessibleName('Earlier days');
  });

  it('starts again from the last six days when it becomes visible on a new day', async () => {
    days['2026-10-31'] = [onDay('2026-10-31', 2023)];
    await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Sat 31 Oct' });

    vi.setSystemTime(new Date(2026, 10, 12, 8));
    act(() => {
      becomes('visible');
    });

    expect(await within(tray()).findByRole('link', { name: 'Tue 10 Nov' })).toBeVisible();
    expect(
      within(tray())
        .getAllByRole('listitem')
        .map((item) => item.textContent),
    ).toEqual(['Tue 10 Nov', 'Earlier days']);
  });

  it('says Nothing in these days when only the newest six it added are empty', async () => {
    days['2026-11-02'] = [onDay('2026-11-02', 2023)];
    await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('link', { name: 'Mon 2 Nov' });
    expect(earlier()).toHaveAccessibleName('Earlier days');

    fireEvent.click(earlier());

    expect(earlier()).toHaveAccessibleName('Earlier days');
    expect(await within(tray()).findByRole('button', { name: 'Nothing in these days' })).toBe(
      earlier(),
    );
  });

  it('shows Nothing in these days for the full time again when the next tap adds none either', async () => {
    await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('button', { name: 'Nothing in these days' });
    await advance(1000);

    fireEvent.click(earlier());
    await within(tray()).findByRole('button', { name: 'Nothing in these days' });
    await advance(1500);
    expect(earlier()).toHaveAccessibleName('Nothing in these days');
    await advance(500);

    expect(earlier()).toHaveAccessibleName('Earlier days');
  });

  it('keeps saying Nothing in these days while six days that answer slowly turn out empty', async () => {
    days['2026-10-24'] = () =>
      new Promise((resolve) =>
        setTimeout(() => {
          resolve(Response.json({ date: '2026-10-24', years: [] }));
        }, 3000),
      );
    await openLanding();
    fireEvent.click(earlier());
    await within(tray()).findByRole('button', { name: 'Nothing in these days' });

    fireEvent.click(earlier());
    await advance(2500);
    expect(earlier()).toHaveAccessibleName('Nothing in these days');
    await advance(500);
    expect(earlier()).toHaveAccessibleName('Nothing in these days');
    await advance(1900);
    expect(earlier()).toHaveAccessibleName('Nothing in these days');
    await advance(200);

    expect(earlier()).toHaveAccessibleName('Earlier days');
  });
});
