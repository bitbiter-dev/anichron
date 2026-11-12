import { fireEvent, screen, within } from '@testing-library/react';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { problemResponse, refreshSucceeds } from '../../test/handlers/auth';
import {
  memory,
  onThisDayAnswers,
  withoutRecentDays,
  yearGroup,
} from '../../test/handlers/flashbacks';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { becomes } from '../../test/visibility';

const ids = {
  a2025: '0199a1b2-c3d4-7e5f-8a9b-0000000a2025',
  b2025: '0199a1b2-c3d4-7e5f-8a9b-0000000b2025',
  a2023: '0199a1b2-c3d4-7e5f-8a9b-0000000a2023',
  a2021: '0199a1b2-c3d4-7e5f-8a9b-0000000a2021',
  b2021: '0199a1b2-c3d4-7e5f-8a9b-0000000b2021',
};
const preview = (id: string) => `/api/v1/proxies/${id}/preview?v=0123456789abcdef`;
const storyName = 'Memories of November 11, 2026';

const threeYears = () => [
  yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025)),
  yearGroup(2023, memory(ids.a2023, 2023)),
  yearGroup(2021, memory(ids.a2021, 2021), memory(ids.b2021, 2021)),
];

let requested: string[];
let years: ReturnType<typeof threeYears>;

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

const galleryOf = (name: string) =>
  screen.getByRole('link', { name }).closest('ul') as HTMLUListElement;
const story = () => screen.getByRole('dialog', { name: storyName });
const picture = () => story().querySelector('img') as HTMLImageElement;

async function shown() {
  const image = picture();
  fireEvent.load(image);
  await advance(0);
  return image.getAttribute('src');
}

async function openLanding() {
  const rendered = renderApp('/');
  await screen.findByRole('link', { name: '1 year ago · 2025' });
  return rendered;
}

async function openStory(path: string) {
  const rendered = renderApp(path);
  await screen.findByRole('dialog', { name: storyName });
  await vi.waitFor(() => {
    expect(picture()).not.toBeNull();
  });
  return rendered;
}

beforeEach(() => {
  requested = [];
  years = threeYears();
  vi.setSystemTime(new Date(2026, 10, 11, 8));
  server.use(
    refreshSucceeds(),
    onThisDayAnswers(
      withoutRecentDays((date) => {
        requested.push(date);
        return years;
      }),
    ),
  );
});

describe('opening a story', () => {
  it('opens a dialog at the year group tapped', async () => {
    const { router, container } = await openLanding();

    fireEvent.click(screen.getByRole('link', { name: '3 years ago · 2023' }));

    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    expect(router.state.location.href).toBe('/day/2026-11-11?year=2023');
    expect(picture().getAttribute('src')).toBe(preview(ids.a2023));
    expect(within(story()).getByText('3 years ago · 2023')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('opens a reload at the year group in the address, from its first memory', async () => {
    await openStory('/day/2026-11-11?year=2021');

    expect(picture().getAttribute('src')).toBe(preview(ids.a2021));
    expect(within(story()).getByText('5 years ago · 2021')).toBeVisible();
  });

  it.each(['/day/2026-11-11?year=2024', '/day/2026-11-11?year=soon', '/day/2026-11-11'])(
    'opens %s at the first year group',
    async (path) => {
      await openStory(path);

      expect(picture().getAttribute('src')).toBe(preview(ids.a2025));
    },
  );

  it('counts the years from the day of the story', async () => {
    renderApp('/day/2024-11-11?year=2023');

    expect(
      await screen.findByRole('dialog', { name: 'Memories of November 11, 2024' }),
    ).toBeVisible();
    expect(screen.getByText('1 year ago · 2023')).toBeVisible();
    expect(requested).toContain('2024-11-11');
  });

  it.each(['/no-such-page', '/day/2026-02-30?year=2023', '/day/2026-1-1', '/day/nonsense'])(
    'treats %s as an unknown page',
    async (path) => {
      const { container } = renderApp(path);

      expect(
        await screen.findByRole('heading', { level: 1, name: 'Page not found' }),
      ).toBeVisible();
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(await axeResults(container)).toHaveNoViolations();
    },
  );

  it('says so when the day cannot be loaded, and Try again plays it', async () => {
    let failing = true;
    server.use(
      onThisDayAnswers(() =>
        failing ? problemResponse(503, 'service_unavailable') : threeYears(),
      ),
    );
    const { container } = renderApp('/day/2026-11-11?year=2023');
    await screen.findByRole('dialog', { name: storyName });
    await advance(10_000);

    expect(within(story()).getByText("Can't reach Anichron. Check your connection.")).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();

    failing = false;
    fireEvent.click(within(story()).getByRole('button', { name: 'Try again' }));
    await advance(0);
    expect(picture().getAttribute('src')).toBe(preview(ids.a2023));
  });

  it('closes at once on a day without memories', async () => {
    years = [];
    const { router } = renderApp('/day/2026-11-11?year=2023');

    expect(await screen.findByText('Nothing from this day in earlier years')).toBeVisible();
    expect(router.state.location.href).toBe('/');
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});

describe('a story playing', () => {
  it('shows each memory for 5 s from the moment its picture is decoded', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await advance(30_000);
    expect(picture().getAttribute('src')).toBe(preview(ids.a2025));

    await shown();
    await advance(4999);
    expect(picture().getAttribute('src')).toBe(preview(ids.a2025));
    await advance(1);
    expect(picture().getAttribute('src')).toBe(preview(ids.b2025));
  });

  it.each(['Video', 'LivePhoto'])(
    'shows a %s as its preview for 5 s, like a photo',
    async (mediaType) => {
      years = [
        yearGroup(
          2025,
          memory(ids.a2025, 2025, {
            mediaType,
            proxies: {
              thumbnail: `proxies/${ids.a2025}/thumbnail?v=0123456789abcdef`,
              preview: `proxies/${ids.a2025}/preview?v=0123456789abcdef`,
              video: `proxies/${ids.a2025}/video?v=0123456789abcdef`,
            },
          }),
          memory(ids.b2025, 2025),
        ),
      ];
      await openStory('/day/2026-11-11?year=2025');

      expect(story().querySelectorAll('img')).toHaveLength(1);
      expect(story().querySelector('video')).toBeNull();
      await shown();
      await advance(4999);
      expect(picture().getAttribute('src')).toBe(preview(ids.a2025));
      await advance(1);
      expect(picture().getAttribute('src')).toBe(preview(ids.b2025));
    },
  );

  it('shows a memory without a preview for 5 s from the moment it is on screen', async () => {
    const bare = (id: string) =>
      memory(id, 2025, { proxies: { thumbnail: null, preview: null, video: null } });
    years = [
      yearGroup(2025, bare(ids.a2025), memory(ids.b2025, 2025)),
      yearGroup(2023, bare(ids.a2023)),
      yearGroup(2021, memory(ids.a2021, 2021)),
    ];
    renderApp('/day/2026-11-11?year=2025');
    await screen.findByRole('dialog', { name: storyName });
    await vi.waitFor(() => {
      expect(story().querySelectorAll('ol > li')).toHaveLength(2);
    });
    expect(picture()).toBeNull();

    await advance(5000);
    expect(picture().getAttribute('src')).toBe(preview(ids.b2025));
    await shown();
    await advance(5000);
    expect(picture()).toBeNull();
    expect(within(story()).getByText('3 years ago · 2023')).toBeVisible();
    await advance(4999);
    expect(picture()).toBeNull();
    await advance(1);
    expect(picture().getAttribute('src')).toBe(preview(ids.a2021));
  });

  it('counts a picture that loads but fails to decode as shown', async () => {
    await openStory('/day/2026-11-11?year=2025');
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockRejectedValue(new Error('EncodingError'));

    await shown();
    await advance(5000);

    expect(picture().getAttribute('src')).toBe(preview(ids.b2025));
  });

  it('plays year group after year group, and closes back to the landing screen after the last', async () => {
    const { router } = await openLanding();
    fireEvent.click(screen.getByRole('link', { name: '1 year ago · 2025' }));
    await screen.findByRole('dialog', { name: storyName });

    const played: (string | null)[] = [];
    const labels: (string | null)[] = [];
    for (let memories = 0; memories < 5; memories++) {
      labels.push(story().querySelector('p')?.textContent ?? null);
      played.push(await shown());
      await advance(5000);
    }

    expect(played).toEqual([
      preview(ids.a2025),
      preview(ids.b2025),
      preview(ids.a2023),
      preview(ids.a2021),
      preview(ids.b2021),
    ]);
    expect(labels).toEqual([
      '1 year ago · 2025',
      '1 year ago · 2025',
      '3 years ago · 2023',
      '5 years ago · 2021',
      '5 years ago · 2021',
    ]);
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
    expect(router.history.canGoBack()).toBe(false);
    expect(screen.getByRole('link', { name: '1 year ago · 2025' })).toBeVisible();
  });

  it('closes after the last memory of a reloaded story too', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    await shown();
    await advance(5000);
    await shown();
    await advance(5000);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
  });

  it('holds the progress of a memory still until its picture is decoded', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const segment = (index: number) =>
      story().querySelectorAll('ol > li > span')[index] as HTMLElement;

    const states = () =>
      [...story().querySelectorAll('ol > li > span')].map((span) =>
        span.getAttribute('data-state'),
      );
    expect(states()).toEqual(['playing', 'next']);
    expect(segment(0).style.animationDuration).toBe('5000ms');
    expect(segment(0).style.animationPlayState).toBe('paused');
    await shown();
    expect(segment(0).style.animationPlayState).toBe('running');
    await advance(5000);
    expect(segment(1).style.animationPlayState).toBe('paused');
    expect(states()).toEqual(['seen', 'playing']);
    await shown();
    await advance(5000);
    expect(segment(0).style.animationPlayState).toBe('paused');
  });

  it('shows one progress segment per memory of the current year group', async () => {
    await openStory('/day/2026-11-11?year=2025');
    expect(story().querySelectorAll('ol > li')).toHaveLength(2);

    for (let memories = 0; memories < 2; memories++) {
      await shown();
      await advance(5000);
    }

    expect(story().querySelectorAll('ol > li')).toHaveLength(1);
  });

  it('keeps its order while open, even when the day changes on the server', async () => {
    await openStory('/day/2026-11-11?year=2025');
    years = [yearGroup(2025, memory(ids.b2025, 2025), memory(ids.a2025, 2025))];
    act(() => {
      becomes('visible');
    });
    await advance(0);
    expect(requested).toHaveLength(2);

    await shown();
    await advance(5000);
    expect(picture().getAttribute('src')).toBe(preview(ids.b2025));
    await shown();
    await advance(5000);
    expect(picture().getAttribute('src')).toBe(preview(ids.a2023));
  });

  it('keeps its day across midnight', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2025');

    vi.setSystemTime(new Date(2027, 0, 1, 0, 1));
    act(() => {
      becomes('visible');
    });
    await advance(0);

    expect(screen.getByRole('dialog', { name: storyName })).toBeVisible();
    expect(story().querySelector('p')?.textContent).toBe('1 year ago · 2025');
    expect(router.state.location.href).toBe('/day/2026-11-11?year=2025');
  });
});

describe('closing a story', () => {
  it.each([
    [
      'the close button',
      () => {
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
      },
    ],
    [
      'Escape',
      () => {
        fireEvent.keyDown(story(), { key: 'Escape' });
      },
    ],
  ])('with %s goes back to the landing screen', async (_, close) => {
    const { router } = await openLanding();
    fireEvent.click(screen.getByRole('link', { name: '3 years ago · 2023' }));
    await screen.findByRole('dialog', { name: storyName });

    close();
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
    expect(router.history.canGoBack()).toBe(false);
  });

  it("with the browser's back button closes it", async () => {
    const { router } = await openLanding();
    fireEvent.click(screen.getByRole('link', { name: '3 years ago · 2023' }));
    await screen.findByRole('dialog', { name: storyName });

    act(() => {
      router.history.back();
    });
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
  });

  it('reloads the day for the landing screen', async () => {
    await openLanding();
    fireEvent.click(screen.getByRole('link', { name: '3 years ago · 2023' }));
    await screen.findByRole('dialog', { name: storyName });
    years = [yearGroup(2023, memory(ids.a2023, 2023))];
    expect(requested).toEqual(['2026-11-11']);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));

    await advance(0);
    expect(requested).toEqual(['2026-11-11', '2026-11-11']);
    await vi.waitFor(() => {
      expect(screen.queryByRole('link', { name: '1 year ago · 2025' })).toBeNull();
    });
    expect(screen.getByRole('link', { name: '3 years ago · 2023' })).toBeVisible();
  });

  it('stops its clock', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    await shown();
    await advance(5000);
    await shown();

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);
    const closed = router.history.location.state.__TSR_key;
    await advance(5000);

    expect(router.history.location.state.__TSR_key).toBe(closed);
  });

  it('reloads only its own day', async () => {
    renderApp('/day/2024-11-11?year=2023');
    await screen.findByRole('dialog', { name: 'Memories of November 11, 2024' });
    await vi.waitFor(() => {
      expect(requested.toSorted()).toEqual(['2024-11-11', '2026-11-11']);
    });

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(1000);

    expect(requested.filter((date) => date === '2026-11-11')).toHaveLength(1);
  });

  it('leaves the landing screen scrolled where it was', async () => {
    await openLanding();
    const gallery = galleryOf('3 years ago · 2023');
    gallery.scrollLeft = 120;
    Object.defineProperty(window, 'scrollY', { value: 300, configurable: true });
    fireEvent.scroll(document);
    const scrollTo = vi.spyOn(window, 'scrollTo');

    fireEvent.click(screen.getByRole('link', { name: '3 years ago · 2023' }));
    await screen.findByRole('dialog', { name: storyName });
    expect(scrollTo).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);

    expect(galleryOf('3 years ago · 2023')).toBe(gallery);
    expect(gallery.scrollLeft).toBe(120);
    expect(scrollTo).toHaveBeenLastCalledWith(expect.objectContaining({ top: 300 }));
  });
});
