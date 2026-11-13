import { fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { interactionAnswers, type Interacted } from '../../test/handlers/assets';
import { problemResponse, refreshSucceeds } from '../../test/handlers/auth';
import { memory, onThisDayAnswers, todayAnd, yearGroup } from '../../test/handlers/flashbacks';
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

const user = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime.bind(vi) });
const galleryOf = (name: string) =>
  screen.getByRole('link', { name }).closest('ul') as HTMLUListElement;
const story = () => screen.getByRole('dialog', { name: storyName });
const button = (name: string) => within(story()).getByRole('button', { name });
const showing = () => picture().getAttribute('src');
async function swipe(x: number, y: number, from = middle()) {
  const at = (step: number) => ({
    clientX: 200 + x * step,
    clientY: 300 + y * step,
    pointerId: 1,
    isPrimary: true,
    button: 0,
  });
  fireEvent.pointerDown(from, at(0));
  for (const step of [0.5, 1]) {
    fireEvent.pointerMove(from, at(step));
    await advance(16);
  }
  fireEvent.pointerUp(from, at(1));
  await advance(2000);
}

const middle = () => button('Previous').nextElementSibling as HTMLElement;
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
      todayAnd(['2024-11-11', '2026-11-11'], (date) => {
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

describe('moving through a story', () => {
  it('goes to the next memory and back with the Next and Previous buttons', async () => {
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().click(button('Next'));
    expect(showing()).toBe(preview(ids.b2025));
    await user().click(button('Previous'));
    expect(showing()).toBe(preview(ids.a2025));
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it("goes back from a year group's first memory to the previous year group's last", async () => {
    await openStory('/day/2026-11-11?year=2021');
    await shown();

    await user().click(button('Previous'));

    expect(showing()).toBe(preview(ids.a2023));
    await user().click(button('Previous'));
    expect(showing()).toBe(preview(ids.b2025));
    expect(within(story()).getByText('1 year ago · 2025')).toBeVisible();
  });

  it('goes back within a later year group', async () => {
    await openStory('/day/2026-11-11?year=2021');
    await user().click(button('Next'));

    await user().click(button('Previous'));

    expect(showing()).toBe(preview(ids.a2021));
  });

  it("starts the story's first memory over when going back from it", async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(3000);

    await user().click(button('Previous'));
    expect(showing()).toBe(preview(ids.a2025));
    await advance(30_000);
    expect(showing()).toBe(preview(ids.a2025));

    await shown();
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('closes when going forward from the last memory', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    await shown();
    await user().click(button('Next'));
    await shown();

    await user().click(button('Next'));
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
  });

  it('gives a memory its full 5 s again when returning to it', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(3000);
    await user().click(button('Next'));
    await shown();
    await advance(3000);

    await user().click(button('Previous'));
    await shown();
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('starts the next memory on its own clock, not the one it left', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(3000);

    await user().click(button('Next'));
    await advance(2000);
    expect(showing()).toBe(preview(ids.b2025));
    await shown();
    await advance(4999);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('starts no clock from a picture it moved away from before it decoded', async () => {
    await openStory('/day/2026-11-11?year=2025');

    fireEvent.load(picture());
    fireEvent.click(button('Next'));
    await advance(30_000);

    expect(showing()).toBe(preview(ids.b2025));
  });

  it('starts the progress of a memory over when returning to it', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    const first = story().querySelector('ol > li > span');

    await user().click(button('Previous'));

    expect(story().querySelector('ol > li > span')).not.toBe(first);
    expect(story().querySelector('ol > li > span')?.getAttribute('data-state')).toBe('playing');
  });

  it('goes to the next memory from a tap in the middle third, which is no extra button', async () => {
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();

    expect(
      within(story())
        .getAllByRole('button')
        .map((zone) => zone.getAttribute('aria-label')),
    ).toEqual(['Previous', 'Next', 'Star', 'Like', 'Hide', 'Close']);
    await user().click(middle());
    expect(showing()).toBe(preview(ids.b2025));
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it.each([
    ['{ArrowRight}', preview(ids.b2025)],
    ['{Shift>}{ArrowRight}{/Shift}', preview(ids.a2023)],
    ['{ArrowRight}{ArrowRight}{ArrowLeft}', preview(ids.b2025)],
    ['{Shift>}{ArrowRight}{ArrowRight}{ArrowLeft}{/Shift}', preview(ids.a2023)],
    ['{Alt>}{ArrowRight}{/Alt}', preview(ids.a2025)],
    ['{Control>}{ArrowRight}{/Control}', preview(ids.a2025)],
    ['{Meta>}{ArrowRight}{/Meta}', preview(ids.a2025)],
    ['{ArrowDown}x', preview(ids.a2025)],
  ])('moves with the keys %s', async (keys, shows) => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().keyboard(keys);

    expect(showing()).toBe(shows);
  });

  it('moves when a zone is pressed with Enter or Space', async () => {
    await openStory('/day/2026-11-11?year=2025');
    button('Next').focus();

    await user().keyboard('{Enter}');
    expect(showing()).toBe(preview(ids.b2025));
    button('Previous').focus();
    await user().keyboard(' ');
    expect(showing()).toBe(preview(ids.a2025));
  });

  it('stops listening to the keys once closed', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    await user().click(button('Next'));
    await user().click(button('Close'));
    await advance(0);
    const closed = router.history.location.state.__TSR_key;

    await user().keyboard('{ArrowRight}{ArrowRight}');
    await advance(0);

    expect(router.history.location.state.__TSR_key).toBe(closed);
  });

  it('moves with the keys while a zone has focus, and closes with Escape', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2025');
    button('Next').focus();

    await user().keyboard('{ArrowRight}');
    expect(showing()).toBe(preview(ids.b2025));
    await user().keyboard('{Escape}');
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
  });
});

describe('swiping through a story', () => {
  it.each([
    ['left to the next year group', -60, 0, preview(ids.a2021)],
    ['right to the previous year group', 60, 59, preview(ids.a2025)],
    ['left, not along a short drag', -59, 0, preview(ids.a2023)],
    ['right, not along a short drag', 59, 0, preview(ids.a2023)],
    ['up, not at all', 0, -200, preview(ids.a2023)],
    ['down along a short drag, not at all', 0, 59, preview(ids.a2023)],
    ['diagonally down, not sideways', -60, 60, null],
  ])('goes %s', async (_, x, y, shows) => {
    await openStory('/day/2026-11-11?year=2023');

    await swipe(x, y);
    await advance(0);

    expect(screen.queryByRole('dialog') && showing()).toBe(shows);
  });

  it('closes with a swipe or drag down', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2023');

    await swipe(0, 60, button('Next'));
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
  });

  it('closes when swiping past the last year group', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');

    await swipe(-60, 0);
    await advance(0);

    expect(router.state.location.href).toBe('/');
  });

  it('starts the first year group over when swiping back from it', async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Next'));

    await swipe(60, 0);

    expect(showing()).toBe(preview(ids.a2025));
  });

  it('moves only by the swipe when it starts and ends on one zone', async () => {
    await openStory('/day/2026-11-11?year=2025');
    vi.spyOn(button('Next'), 'getBoundingClientRect').mockReturnValue(
      DOMRect.fromRect({ x: 0, y: 0, width: 1000, height: 1000 }),
    );

    await swipe(-60, 0, button('Next'));
    expect(showing()).toBe(preview(ids.a2023));
    await user().click(button('Next'));
    expect(showing()).toBe(preview(ids.a2021));
  });

  it('takes a press with Enter right after a swipe', async () => {
    await openStory('/day/2026-11-11?year=2025');

    await swipe(-60, 0);
    button('Next').focus();
    await user().keyboard('{Enter}');

    expect(showing()).toBe(preview(ids.a2021));
  });

  it('keeps the browser from scrolling, selecting or showing a menu while swiping', async () => {
    await openStory('/day/2026-11-11?year=2025');

    const player = middle().parentElement?.parentElement as HTMLElement;
    expect(player.style.touchAction).toBe('none');
    expect(player).toHaveClass('select-none', '[-webkit-touch-callout:none]');
  });
});

describe('starring, liking and hiding', () => {
  let sent: Interacted[];
  let answer: (interacted: Interacted) => Response | Promise<Response> | undefined;
  const refused = () => problemResponse(500, 'unknown');
  const segments = () => story().querySelectorAll('ol > li');
  const toast = () => screen.getByRole('alertdialog');

  let saved: Map<string, object>;

  const withSaved = (days: ReturnType<typeof threeYears>) =>
    days
      .map((group) => ({
        ...group,
        assets: group.assets
          .map((asset) => ({
            ...asset,
            interaction: { ...asset.interaction, ...saved.get(asset.id) },
          }))
          .filter((asset) => !asset.interaction.hidden),
      }))
      .filter((group) => group.assets.length > 0);

  beforeEach(() => {
    sent = [];
    saved = new Map();
    answer = () => undefined;
    server.use(
      onThisDayAnswers(
        todayAnd(['2024-11-11', '2026-11-11'], (date) => {
          requested.push(date);
          return withSaved(years);
        }),
      ),
      interactionAnswers(sent, async (interacted) => {
        const failed = await answer(interacted);
        if (!failed)
          saved.set(interacted.id, { ...saved.get(interacted.id), ...(interacted.body as object) });
        return failed;
      }),
    );
  });

  function later() {
    let settle: (response?: Response) => void = () => undefined;
    const settled = new Promise<Response | undefined>((resolve) => {
      settle = resolve;
    });
    answer = () => settled as Promise<Response>;
    return (response?: Response) => {
      settle(response);
    };
  }

  it.each([
    ['Star', 'starred'],
    ['Like', 'liked'],
  ])('%s toggles at once and sends only that field', async (name, flag) => {
    await openStory('/day/2026-11-11?year=2025');
    const respond = later();

    fireEvent.click(button(name));
    await advance(0);

    expect(button(name)).toHaveAttribute('aria-pressed', 'true');
    respond();
    await advance(0);
    fireEvent.click(button(name));
    await advance(0);
    expect(button(name)).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(button(name));
    await advance(0);
    fireEvent.click(button('Next'));
    expect(button(name)).toHaveAttribute('aria-pressed', 'false');
    expect(sent).toEqual([
      { id: ids.a2025, body: { [flag]: true } },
      { id: ids.a2025, body: { [flag]: false } },
      { id: ids.a2025, body: { [flag]: true } },
    ]);
  });

  it('shows what each memory has saved', async () => {
    years = [
      yearGroup(
        2025,
        memory(ids.a2025, 2025, {
          interaction: { starred: true, liked: false, hidden: false, displayWeight: 1 },
        }),
        memory(ids.b2025, 2025, {
          interaction: { starred: false, liked: true, hidden: false, displayWeight: 1 },
        }),
      ),
    ];
    await openStory('/day/2026-11-11?year=2025');

    expect(button('Star')).toHaveAttribute('aria-pressed', 'true');
    expect(button('Like')).toHaveAttribute('aria-pressed', 'false');
    fireEvent.click(button('Next'));
    expect(button('Star')).toHaveAttribute('aria-pressed', 'false');
    expect(button('Like')).toHaveAttribute('aria-pressed', 'true');
  });

  it('neither moves nor restarts the story when a control is pressed', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(4000);

    await user().click(button('Star'));
    await user().click(button('Like'));
    await advance(999);

    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it("rolls back a change that couldn't be saved and says so, politely, for 5 s", async () => {
    answer = refused;
    await openStory('/day/2026-11-11?year=2025');

    fireEvent.click(button('Like'));
    await advance(0);

    expect(button('Like')).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('status')).toHaveTextContent("Couldn't save");
    expect(toast()).toBeVisible();
    await advance(4999);
    expect(screen.queryByRole('alertdialog')).not.toBeNull();
    await advance(1);
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });

  it("rolls back a change that couldn't be saved while the day can't be reloaded either", async () => {
    answer = refused;
    await openStory('/day/2026-11-11?year=2025');
    server.use(onThisDayAnswers(() => problemResponse(503, 'service_unavailable')));

    fireEvent.click(button('Star'));
    await advance(0);

    expect(button('Star')).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('status')).toHaveTextContent("Couldn't save");
  });

  it('rolls back only the change that failed', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const respond = later();
    fireEvent.click(button('Star'));
    await advance(0);
    answer = () => undefined;
    fireEvent.click(button('Like'));
    await advance(0);

    respond(refused());
    await advance(0);

    expect(button('Star')).toHaveAttribute('aria-pressed', 'false');
    expect(button('Like')).toHaveAttribute('aria-pressed', 'true');
  });

  it('keeps a change through a reload of its day that was already on its way, and only that reload', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const reloads = new Map<string, (response: Response) => void>();
    server.use(
      onThisDayAnswers((date) =>
        ['2026-11-11', '2026-11-10'].includes(date) && !reloads.has(date)
          ? new Promise<Response>((resolve) => {
              reloads.set(date, resolve);
            })
          : date === '2026-11-11'
            ? withSaved(years)
            : [],
      ),
    );
    act(() => {
      becomes('visible');
    });
    await advance(0);

    fireEvent.click(button('Star'));
    await advance(0);
    const answer = (date: string, days: ReturnType<typeof threeYears>) => {
      reloads.get(date)?.(Response.json({ date, years: days }));
    };
    answer('2026-11-11', threeYears());
    answer('2026-11-10', [yearGroup(2024, memory(ids.a2025, 2024))]);
    await advance(0);

    expect(button('Star')).toHaveAttribute('aria-pressed', 'true');
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);
    expect(await screen.findByRole('link', { name: 'Yesterday' })).toBeVisible();
  });

  it('reloads the day once its last change is saved, not before', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const respond = later();
    const held = answer;
    answer = (interacted) =>
      'liked' in (interacted.body as object) ? held(interacted) : undefined;
    fireEvent.click(button('Star'));
    fireEvent.click(button('Like'));
    await vi.waitFor(() => {
      expect(saved.has(ids.a2025)).toBe(true);
    });
    await advance(1000);
    expect(sent).toHaveLength(2);
    expect(requested).toHaveLength(1);
    expect(button('Like')).toHaveAttribute('aria-pressed', 'true');

    respond();
    await advance(0);

    expect(requested).toHaveLength(2);
    expect(button('Like')).toHaveAttribute('aria-pressed', 'true');
  });

  it('reloads the landing screen again once a hide made just before closing is saved', async () => {
    await openLanding();
    const first = () =>
      screen
        .getByRole('link', { name: '5 years ago · 2021' })
        .querySelector('img')
        ?.getAttribute('src');
    fireEvent.click(screen.getByRole('link', { name: '5 years ago · 2021' }));
    await screen.findByRole('dialog', { name: storyName });
    const respond = later();
    fireEvent.click(button('Hide'));
    fireEvent.click(button('Close'));
    await advance(0);
    expect(first()).toContain(ids.a2021);

    respond();
    await advance(0);

    await vi.waitFor(() => {
      expect(first()).toContain(ids.b2021);
    });
  });

  it('keeps showing a memory the server has since left out, as it was', async () => {
    years = [
      yearGroup(
        2025,
        memory(ids.a2025, 2025, {
          interaction: { starred: true, liked: false, hidden: false, displayWeight: 1 },
        }),
      ),
    ];
    await openStory('/day/2026-11-11?year=2025');
    years = [];
    act(() => {
      becomes('visible');
    });
    await advance(0);

    expect(requested).toHaveLength(2);
    expect(showing()).toBe(preview(ids.a2025));
    expect(button('Star')).toHaveAttribute('aria-pressed', 'true');
  });

  it('does not reorder the open story when a later memory is starred', async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Next'));
    fireEvent.click(button('Star'));
    await advance(0);

    fireEvent.click(button('Previous'));

    expect(showing()).toBe(preview(ids.a2025));
  });

  it('hides a memory at once, moves to the next and offers Undo for 5 s', async () => {
    await openStory('/day/2026-11-11?year=2025');

    fireEvent.click(button('Hide'));

    expect(showing()).toBe(preview(ids.b2025));
    expect(segments()).toHaveLength(1);
    await advance(0);
    expect(sent).toEqual([{ id: ids.a2025, body: { hidden: true } }]);
    expect(screen.getByRole('status')).toHaveTextContent('Memory hidden');
    expect(within(toast()).getByRole('button', { name: 'Undo' })).toBeVisible();
    fireEvent.click(button('Previous'));
    expect(showing()).toBe(preview(ids.b2025));
    await advance(4999);
    expect(screen.queryByRole('alertdialog')).not.toBeNull();
    await advance(1);
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });

  it('gives the memory after a hidden one its own clock', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(4000);

    fireEvent.click(button('Hide'));
    await advance(4000);
    expect(showing()).toBe(preview(ids.b2025));
    await shown();
    await advance(4999);
    expect(showing()).toBe(preview(ids.b2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.a2023));
  });

  it('puts a hidden memory back where it was with Undo and shows it again', async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    await advance(0);
    expect(await axeResults(document.body)).toHaveNoViolations();

    await user().click(within(toast()).getByRole('button', { name: 'Undo' }));
    await advance(0);

    expect(showing()).toBe(preview(ids.a2025));
    expect(segments()).toHaveLength(2);
    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(sent).toEqual([
      { id: ids.a2025, body: { hidden: true } },
      { id: ids.a2025, body: { hidden: false } },
    ]);
    fireEvent.click(button('Next'));
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('puts each memory back at its own place, whatever was hidden since', async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    fireEvent.click(button('Hide'));
    await advance(0);
    expect(showing()).toBe(preview(ids.a2023));

    fireEvent.click(screen.getAllByRole('button', { name: 'Undo' }).at(-1) as HTMLElement);

    expect(showing()).toBe(preview(ids.a2025));
    expect(segments()).toHaveLength(1);
    fireEvent.click(button('Next'));
    expect(showing()).toBe(preview(ids.a2023));
    fireEvent.click(button('Previous'));
    expect(showing()).toBe(preview(ids.a2025));
  });

  it('leaves out a year group whose memories are all hidden, until Undo', async () => {
    await openStory('/day/2026-11-11?year=2023');
    fireEvent.click(button('Hide'));
    await advance(0);
    expect(showing()).toBe(preview(ids.a2021));
    expect(within(story()).getByText('5 years ago · 2021')).toBeVisible();

    fireEvent.click(button('Previous'));
    expect(showing()).toBe(preview(ids.b2025));
    await swipe(-60, 0);
    expect(showing()).toBe(preview(ids.a2021));
    await swipe(60, 0);
    expect(showing()).toBe(preview(ids.a2025));
    fireEvent.click(within(toast()).getByRole('button', { name: 'Undo' }));
    expect(showing()).toBe(preview(ids.a2023));
  });

  it('goes back from a year group past the hidden memories at the end of the one before', async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Next'));
    fireEvent.click(button('Hide'));
    await advance(0);
    expect(showing()).toBe(preview(ids.a2023));

    fireEvent.click(button('Previous'));

    expect(showing()).toBe(preview(ids.a2025));
  });

  it('closes when the last memory is hidden', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    fireEvent.click(button('Next'));

    fireEvent.click(button('Hide'));
    await advance(0);

    expect(router.state.location.href).toBe('/');
    expect(sent).toEqual([{ id: ids.b2021, body: { hidden: true } }]);
  });

  it("brings a memory back when its hide couldn't be saved, and says so", async () => {
    await openStory('/day/2026-11-11?year=2025');
    const respond = later();
    fireEvent.click(button('Hide'));
    await advance(0);
    expect(showing()).toBe(preview(ids.b2025));

    respond(refused());
    await advance(0);

    expect(showing()).toBe(preview(ids.a2025));
    expect(segments()).toHaveLength(2);
    expect(screen.getByRole('status')).toHaveTextContent("Couldn't save");
    expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    expect(await axeResults(document.body)).toHaveNoViolations();
  });

  it("hides a memory again when its Undo couldn't be saved", async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    await advance(0);
    answer = refused;

    fireEvent.click(within(toast()).getByRole('button', { name: 'Undo' }));
    await advance(0);

    expect(showing()).toBe(preview(ids.b2025));
    expect(segments()).toHaveLength(1);
    expect(screen.getByRole('status')).toHaveTextContent("Couldn't save");
  });

  it("drops a memory whose Undo couldn't be saved without moving on from where the story is", async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    await advance(0);
    const respond = later();
    fireEvent.click(within(toast()).getByRole('button', { name: 'Undo' }));
    fireEvent.click(button('Next'));
    await shown();
    await advance(3000);

    respond(refused());
    await advance(0);

    expect(showing()).toBe(preview(ids.b2025));
    expect(segments()).toHaveLength(1);
    expect(story().querySelector('ol span')?.getAttribute('data-state')).toBe('playing');
    await advance(1999);
    expect(showing()).toBe(preview(ids.b2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.a2023));
  });

  it("drops a memory whose Undo couldn't be saved while another year group plays", async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    await advance(0);
    const respond = later();
    fireEvent.click(within(toast()).getByRole('button', { name: 'Undo' }));
    fireEvent.keyDown(document, { key: 'ArrowRight', shiftKey: true });
    expect(showing()).toBe(preview(ids.a2023));

    respond(refused());
    await advance(0);

    expect(showing()).toBe(preview(ids.a2023));
    fireEvent.click(button('Previous'));
    expect(showing()).toBe(preview(ids.b2025));
    expect(segments()).toHaveLength(1);
  });

  it('does nothing more once closed when an Undo fails afterwards', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    await advance(0);
    const respond = later();
    fireEvent.click(within(toast()).getByRole('button', { name: 'Undo' }));
    fireEvent.click(button('Next'));
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);

    respond(refused());
    await advance(10_000);

    expect(router.state.location.href).toBe('/');
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('does nothing more once closed when a hide fails afterwards', async () => {
    years = [
      yearGroup(
        2025,
        memory(ids.a2025, 2025, { proxies: { thumbnail: null, preview: null, video: null } }),
      ),
    ];
    const { router } = await openLanding();
    fireEvent.click(screen.getByRole('link', { name: '1 year ago · 2025' }));
    await screen.findByRole('dialog', { name: storyName });
    const respond = later();
    fireEvent.click(button('Hide'));
    await advance(0);
    expect(screen.queryByRole('dialog')).toBeNull();
    act(() => {
      void router.navigate({ to: '/settings' });
    });
    await advance(0);

    respond(refused());
    await advance(10_000);

    expect(router.state.location.href).toBe('/settings');
  });

  it('reaches Undo from the keyboard', async () => {
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Hide'));
    await advance(0);

    await user().keyboard('{F6}{Tab}{Tab}');

    expect(within(toast()).getByRole('button', { name: 'Undo' })).toHaveFocus();
  });
});
