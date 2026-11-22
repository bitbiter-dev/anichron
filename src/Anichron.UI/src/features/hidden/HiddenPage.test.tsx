import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import { refreshSucceeds } from '../../test/handlers/auth';
import { type Interacted, interactionAnswers } from '../../test/handlers/assets';
import { fakeDisplay } from '../../test/display';
import { hold, memory, proxyPath, proxyUrl } from '../../test/handlers/flashbacks';
import { hiddenAnswers, hiddenMemory } from '../../test/handlers/hidden';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { advance, user } from '../../test/time';

const id = (n: number) => `0199a1b2-c3d4-7e5f-8a9b-${String(n).padStart(12, '0')}`;
const at = (month: number, day: number, hour = 9, year = 2026) =>
  new Date(year, month - 1, day, hour);
const takenOn = (day: number) => ({ month: 11, day });
const tileName = (day: number, kind = 'Photo') => `${kind}, taken Nov ${String(day)}, 2019`;

function hidden(n: number, when: Date, overrides = {}) {
  return hiddenMemory(id(n), when, { ...takenOn(n), ...overrides });
}

type Item = ReturnType<typeof hidden>;

function onePage(...items: Item[]) {
  return hiddenAnswers(() => ({ items, next: null }));
}

let sent: Interacted[];
let asked: (string | null)[];

function pages(...all: Item[][]) {
  return hiddenAnswers((after) => {
    asked.push(after);
    const index = after === null ? 0 : Number(after.slice(4));
    return {
      items: all[index] ?? [],
      next: index + 1 < all.length ? `page${String(index + 1)}` : null,
    };
  });
}

function paintsBlurhashes() {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    createImageData: (width: number, height: number) => ({
      width,
      height,
      data: new Uint8ClampedArray(width * height * 4),
    }),
    putImageData: () => undefined,
  } as unknown as CanvasRenderingContext2D);
}

function pageHeight(height: number) {
  Object.defineProperty(document.documentElement, 'scrollHeight', {
    value: height,
    configurable: true,
  });
}

function scrollTo(y: number) {
  act(() => {
    Object.defineProperty(window, 'scrollY', { value: y, configurable: true });
    fireEvent.scroll(window);
  });
}

beforeEach(() => {
  sent = [];
  asked = [];
  vi.setSystemTime(at(11, 18, 10));
  server.use(refreshSucceeds(), interactionAnswers(sent));
});

const headings = () =>
  screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent);
const tile = (name: string) => screen.getByRole('button', { name });
const tilesIn = (heading: string) =>
  within(screen.getByRole('region', { name: heading }))
    .getAllByRole('button')
    .map((button) => button.getAttribute('aria-label'));
const allTiles = () =>
  screen
    .getAllByRole('button', { name: /, taken /, hidden: true })
    .map((button) => button.getAttribute('aria-label'));
const viewer = () => screen.getByRole('dialog', { name: 'Hidden memories' });
const caption = () => within(viewer()).getByText(/^Taken /);

async function openHidden() {
  const rendered = renderApp('/hidden');
  await screen.findAllByRole('button', { name: /, taken / });
  return rendered;
}

async function openViewer(name: string) {
  await user().click(tile(name));
  return viewer();
}

describe('finding the screen', () => {
  it('is linked from Settings and goes back to it', async () => {
    server.use(onePage(hidden(1, at(11, 18))));
    renderApp('/settings');

    await user().click(await screen.findByRole('link', { name: 'Hidden memories' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Hidden memories' })).toBeVisible();
    expect(await screen.findByRole('button', { name: tileName(1) })).toBeVisible();
    await user().click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  });
});

describe('the grid', () => {
  it('lists the newest hidden first, under headings by when each was hidden', async () => {
    server.use(
      onePage(
        hidden(1, at(11, 18, 9)),
        hidden(2, at(11, 18, 0)),
        hidden(3, at(11, 16, 0)),
        hidden(4, at(11, 15, 23)),
        hidden(5, at(11, 1, 0)),
        hidden(6, at(10, 31, 23)),
        hidden(7, at(10, 2)),
        hidden(8, at(12, 25, 9, 2025)),
      ),
    );
    const { container } = await openHidden();

    expect(headings()).toEqual([
      'Today',
      'This week',
      'This month',
      'October 2026',
      'December 2025',
    ]);
    expect(tilesIn('Today')).toEqual([tileName(1), tileName(2)]);
    expect(tilesIn('This week')).toEqual([tileName(3)]);
    expect(tilesIn('This month')).toEqual([tileName(4), tileName(5)]);
    expect(tilesIn('October 2026')).toEqual([tileName(6), tileName(7)]);
    expect(tilesIn('December 2025')).toEqual([tileName(8)]);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('keeps a week that began last month as This week, and starts it on Monday', async () => {
    vi.setSystemTime(at(12, 1, 10));
    server.use(onePage(hidden(1, at(11, 30, 0)), hidden(2, at(11, 29, 23))));
    await openHidden();

    expect(headings()).toEqual(['This week', 'November 2026']);
  });

  it('names Sunday as this week when the week has just ended', async () => {
    vi.setSystemTime(at(11, 22, 10));
    server.use(onePage(hidden(1, at(11, 16, 0)), hidden(2, at(11, 15, 23))));
    await openHidden();

    expect(headings()).toEqual(['This week', 'This month']);
  });

  it('shows each thumbnail over its BlurHash, a Live Photo with its badge, and a video by its poster', async () => {
    paintsBlurhashes();
    server.use(
      onePage(
        hidden(1, at(11, 18), { blurhash: 'LEHV6nWB2yk8pyo0adR*.7kCMdnj' }),
        hidden(2, at(11, 17), {
          mediaType: 'LivePhoto',
          livePhotoVideoUrl: 'proxies/mov/video?v=1',
        }),
        hidden(3, at(11, 16), {
          mediaType: 'Video',
          proxies: {
            thumbnail: `proxies/${id(3)}/thumbnail?v=0123456789abcdef`,
            preview: null,
            video: `proxies/${id(3)}/video?v=0123456789abcdef`,
          },
        }),
      ),
    );
    await openHidden();

    const photo = tile(tileName(1));
    expect(photo.querySelector('canvas')).not.toBeNull();
    fireEvent.load(photo.querySelector('img') as HTMLImageElement);
    await advance(0);
    expect(photo.querySelector('canvas')).toBeNull();
    const live = tile(tileName(2, 'Live Photo'));
    expect(live.querySelector('svg')).not.toBeNull();
    expect(tile(tileName(1)).querySelector('svg')).toBeNull();
    const video = tile(tileName(3, 'Video'));
    expect(video.querySelector('img')).toHaveAttribute('src', proxyUrl(id(3), 'thumbnail'));
    expect(video.querySelector('video')).toBeNull();
  });

  it('shows a burst frame alone, without the count of other frames', async () => {
    server.use(onePage(hidden(1, at(11, 18), { burst: null })));
    await openHidden();

    expect(within(tile(tileName(1))).queryByText(/^\+/)).toBeNull();
  });

  it('says Nothing hidden, with where hidden memories come from, when there are none', async () => {
    server.use(hiddenAnswers(() => ({ items: [], next: null })));
    const { container } = renderApp('/hidden');

    expect(await screen.findByText('Nothing hidden')).toBeVisible();
    expect(screen.getByText('Memories you hide from a story show up here.')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('is busy with a skeleton while the first page loads', async () => {
    const answer = hold();
    server.use(
      hiddenAnswers(async () => {
        await answer.until;
        return { items: [hidden(1, at(11, 18))], next: null };
      }),
    );
    renderApp('/hidden');

    const main = await screen.findByRole('main');
    await advance(1000);
    expect(main).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByRole('button', { name: tileName(1) })).toBeNull();
    answer.release();
    expect(await screen.findByRole('button', { name: tileName(1) })).toBeVisible();
    expect(main).toHaveAttribute('aria-busy', 'false');
  });

  it('offers Try again when the list cannot load', async () => {
    let fails = true;
    server.use(
      hiddenAnswers(() =>
        fails
          ? HttpResponse.json({ status: 500 }, { status: 500 })
          : { items: [hidden(1, at(11, 18))], next: null },
      ),
    );
    renderApp('/hidden');
    await advance(10_000);

    expect(await screen.findByText("Couldn't load your hidden memories.")).toBeVisible();
    fails = false;
    await user().click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('button', { name: tileName(1) })).toBeVisible();
  });

  it('loads more without a scroll while the grid is too short to fill the screen', async () => {
    server.use(pages([hidden(1, at(11, 18))], [hidden(2, at(11, 17))]));
    await openHidden();

    expect(await screen.findByRole('button', { name: tileName(2) })).toBeVisible();
    expect(asked).toEqual([null, 'page1']);
  });

  it('loads the next page as the grid scrolls near its end, and stops after the last', async () => {
    pageHeight(4000);
    server.use(
      pages(
        [hidden(1, at(11, 18)), hidden(2, at(11, 17))],
        [hidden(3, at(11, 16))],
        [hidden(4, at(11, 15))],
      ),
    );
    await openHidden();
    expect(asked).toEqual([null]);

    scrollTo(2431);
    await advance(0);
    expect(asked).toEqual([null]);

    scrollTo(2432);
    expect(await screen.findByRole('button', { name: tileName(3) })).toBeVisible();
    scrollTo(2600);
    expect(await screen.findByRole('button', { name: tileName(4) })).toBeVisible();
    scrollTo(2700);
    await advance(0);

    expect(asked).toEqual([null, 'page1', 'page2']);
    expect(allTiles()).toEqual([tileName(1), tileName(2), tileName(3), tileName(4)]);
  });
});

describe('the viewer', () => {
  it('opens the tapped memory full screen with when it was taken and hidden', async () => {
    server.use(onePage(hidden(1, at(11, 18, 8)), hidden(12, at(11, 15)), hidden(3, at(10, 1))));
    const { container } = await openHidden();

    await openViewer(tileName(12));

    expect(caption()).toHaveTextContent('Taken Nov 12, 2019 · Hidden 3 days ago');
    expect(within(viewer()).getAllByRole('img', { name: 'Photo' })).toHaveLength(3);
    expect(await axeResults(container.ownerDocument.body)).toHaveNoViolations();
  });

  it('scrolls to the tapped memory as it opens', async () => {
    server.use(onePage(hidden(1, at(11, 18)), hidden(2, at(11, 17)), hidden(3, at(11, 16))));
    await openHidden();
    vi.spyOn(Element.prototype, 'clientWidth', 'get').mockReturnValue(390);
    const scrolledTo = vi.spyOn(Element.prototype, 'scrollLeft', 'set');

    await openViewer(tileName(3));

    expect(scrolledTo.mock.calls).toEqual([[780]]);
  });

  it('shows a memory with no preview yet without its BlurHash', async () => {
    paintsBlurhashes();
    server.use(
      onePage(
        hidden(1, at(11, 18), {
          blurhash: 'LEHV6nWB2yk8pyo0adR*.7kCMdnj',
          proxies: { thumbnail: null, preview: null, video: null },
        }),
      ),
    );
    await openHidden();
    await openViewer(tileName(1));

    const slide = within(viewer()).getByRole('img', { name: 'Photo' });
    expect(slide.querySelector('canvas')).toBeNull();
    expect(slide.querySelector('img')).toBeNull();
  });

  it.each([
    [at(11, 18, 0), 'today'],
    [at(11, 17, 23), 'yesterday'],
    [at(10, 20, 9), '29 days ago'],
    [at(10, 19, 9), 'last month'],
    [at(9, 1, 9), '2 months ago'],
    [at(12, 1, 9, 2025), '11 months ago'],
    [at(11, 19, 9, 2025), 'last year'],
    [at(11, 1, 9, 2025), 'last year'],
    [at(1, 1, 9, 2023), '3 years ago'],
  ])('says when it was hidden, %s as %s', async (when, ago) => {
    server.use(onePage(hidden(12, when)));
    await openHidden();

    await openViewer(tileName(12));

    expect(caption()).toHaveTextContent(`Hidden ${ago}`);
  });

  it('moves with the arrow keys and the Previous and Next buttons, stopping at either end', async () => {
    server.use(onePage(hidden(1, at(11, 18)), hidden(2, at(11, 17)), hidden(3, at(11, 16))));
    await openHidden();
    await openViewer(tileName(2));

    await user().keyboard('{ArrowRight}');
    expect(caption()).toHaveTextContent('Taken Nov 3, 2019');
    await user().keyboard('{ArrowRight}');
    expect(caption()).toHaveTextContent('Taken Nov 3, 2019');
    await user().click(within(viewer()).getByRole('button', { name: 'Previous memory' }));
    await user().click(within(viewer()).getByRole('button', { name: 'Previous memory' }));
    await user().click(within(viewer()).getByRole('button', { name: 'Previous memory' }));
    expect(caption()).toHaveTextContent('Taken Nov 1, 2019');
    await user().click(within(viewer()).getByRole('button', { name: 'Next memory' }));
    expect(caption()).toHaveTextContent('Taken Nov 2, 2019');
    await advance(60_000);
    expect(caption()).toHaveTextContent('Taken Nov 2, 2019');
  });

  it('moves on a tap on the left or right half', async () => {
    server.use(onePage(hidden(1, at(11, 18)), hidden(2, at(11, 17)), hidden(3, at(11, 16))));
    await openHidden();
    await openViewer(tileName(1));
    const list = within(viewer()).getByRole('list');
    const tap = (clientX: number) =>
      user().pointer({ keys: '[MouseLeft]', target: list, coords: { clientX } });

    await tap(900);
    expect(caption()).toHaveTextContent('Taken Nov 2, 2019');
    await tap(900);
    await tap(900);
    expect(caption()).toHaveTextContent('Taken Nov 3, 2019');
    await tap(100);
    expect(caption()).toHaveTextContent('Taken Nov 2, 2019');
  });

  it('closes on Esc and on its close button, back to the grid', async () => {
    server.use(onePage(hidden(1, at(11, 18)), hidden(2, at(11, 17))));
    await openHidden();
    act(() => {
      tile(tileName(2)).focus();
    });
    await user().keyboard('{Enter}');
    expect(caption()).toHaveTextContent('Taken Nov 2, 2019');

    await user().keyboard('{Escape}');
    await advance(100);
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(tile(tileName(2))).toHaveFocus();

    await openViewer(tileName(1));
    await user().click(within(viewer()).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('offers only Unhide, no star or like', async () => {
    server.use(onePage(hidden(1, at(11, 18))));
    await openHidden();
    await openViewer(tileName(1));

    expect(within(viewer()).getByRole('button', { name: 'Unhide' })).toBeVisible();
    expect(within(viewer()).queryByRole('button', { name: 'Star' })).toBeNull();
    expect(within(viewer()).queryByRole('button', { name: 'Like' })).toBeNull();
  });

  it('fetches the next page as it nears the end of what is loaded', async () => {
    pageHeight(100_000);
    server.use(
      pages(
        [1, 2, 3, 4, 5].map((n) => hidden(n, at(11, 18, 10 - n))),
        [hidden(6, at(11, 17))],
      ),
    );
    await openHidden();
    await openViewer(tileName(1));

    await user().keyboard('{ArrowRight}');
    await advance(0);
    expect(asked).toEqual([null]);
    await user().keyboard('{ArrowRight}');
    await waitFor(() => {
      expect(within(viewer()).getAllByRole('img', { name: 'Photo' })).toHaveLength(6);
    });
    expect(asked).toEqual([null, 'page1']);
    await user().keyboard('{ArrowRight}{ArrowRight}{ArrowRight}');
    expect(caption()).toHaveTextContent('Taken Nov 6, 2019');
  });

  it('shows the compact preview on a phone that decodes JPEG XL', async () => {
    const { proxies } = memory(id(1), 2019);
    server.use(
      onePage(
        hidden(1, at(11, 18), {
          proxies: { ...proxies, compactPreview: proxyPath(id(1), 'compact-preview') },
        }),
      ),
    );
    renderApp('/hidden', undefined, fakeDisplay({ jpegXl: true, longEdge: 844 }));
    await screen.findAllByRole('button', { name: /, taken / });
    await openViewer(tileName(1));

    const photo = within(viewer()).getByRole('img', { name: 'Photo' });
    expect(photo.querySelector('img')).toHaveAttribute('src', proxyUrl(id(1), 'compact-preview'));
  });

  it('shows a photo at full preview size and its BlurHash until it decodes', async () => {
    paintsBlurhashes();
    server.use(onePage(hidden(1, at(11, 18), { blurhash: 'LEHV6nWB2yk8pyo0adR*.7kCMdnj' })));
    await openHidden();
    await openViewer(tileName(1));

    const photo = within(viewer()).getByRole('img', { name: 'Photo' });
    const image = photo.querySelector('img') as HTMLImageElement;
    expect(image).toHaveAttribute('src', proxyUrl(id(1), 'preview'));
    expect(photo.querySelector('canvas')).not.toBeNull();
    fireEvent.load(image);
    await advance(0);
    expect(photo.querySelector('canvas')).toBeNull();
  });

  it('says a photo that cannot load could not be loaded', async () => {
    server.use(onePage(hidden(1, at(11, 18))));
    await openHidden();
    await openViewer(tileName(1));

    const picture = () =>
      within(viewer()).getByRole('img', { name: 'Photo' }).querySelector('img') as HTMLImageElement;
    fireEvent.error(picture());
    await advance(0);
    expect(within(viewer()).queryByText("Couldn't load this memory")).toBeNull();
    await waitFor(() => {
      expect(picture()).not.toBeNull();
    });
    fireEvent.error(picture());

    expect(await within(viewer()).findByText("Couldn't load this memory")).toBeVisible();
  });

  it('loads previews only for the memory shown and its neighbours', async () => {
    server.use(onePage(...[1, 2, 3, 4, 5].map((n) => hidden(n, at(11, 18, 10 - n)))));
    await openHidden();
    await openViewer(tileName(3));

    expect(
      within(viewer())
        .getAllByRole('img', { name: 'Photo' })
        .map((slide) => slide.querySelector('img')?.getAttribute('src') ?? null),
    ).toEqual([
      null,
      proxyUrl(id(2), 'preview'),
      proxyUrl(id(3), 'preview'),
      proxyUrl(id(4), 'preview'),
      null,
    ]);
  });
});

describe('a video and a Live Photo in the viewer', () => {
  const videoMemory = (n: number, when: Date) =>
    hidden(n, when, {
      mediaType: 'Video',
      durationSeconds: 12,
      proxies: {
        thumbnail: `proxies/${id(n)}/thumbnail?v=0123456789abcdef`,
        preview: `proxies/${id(n)}/preview?v=0123456789abcdef`,
        video: `proxies/${id(n)}/video?v=0123456789abcdef`,
      },
    });
  const liveMemory = (n: number, when: Date) =>
    hidden(n, when, {
      mediaType: 'LivePhoto',
      livePhotoVideoUrl: `proxies/mov${String(n)}/video?v=1`,
    });
  const videoIn = (slide: HTMLElement) => slide.closest('li')?.querySelector('video') ?? null;
  const slides = () => within(viewer()).getAllByRole('listitem');

  it('plays a video muted, with sound on request, over its poster', async () => {
    server.use(onePage(videoMemory(1, at(11, 18))));
    await openHidden();
    await openViewer(tileName(1, 'Video'));

    const video = videoIn(slides()[0] as HTMLElement) as HTMLVideoElement;
    expect(video).toHaveAttribute('src', proxyUrl(id(1), 'video'));
    expect(video).toHaveAttribute('poster', proxyUrl(id(1), 'preview'));
    expect(video.autoplay).toBe(true);
    expect(video.muted).toBe(true);
    await user().click(within(viewer()).getByRole('button', { name: 'Turn sound on' }));
    expect(video.muted).toBe(false);
    await user().click(within(viewer()).getByRole('button', { name: 'Turn sound off' }));
    expect(video.muted).toBe(true);
  });

  it('plays only the video of the memory shown', async () => {
    server.use(onePage(videoMemory(1, at(11, 18)), videoMemory(2, at(11, 17))));
    await openHidden();
    await openViewer(tileName(1, 'Video'));

    expect(slides().map((slide) => videoIn(slide) !== null)).toEqual([true, false]);
    await user().keyboard('{ArrowRight}');
    expect(slides().map((slide) => videoIn(slide) !== null)).toEqual([false, true]);
  });

  it('shows the poster and says so when a video cannot play', async () => {
    server.use(onePage(videoMemory(1, at(11, 18))));
    await openHidden();
    await openViewer(tileName(1, 'Video'));
    const slide = slides()[0] as HTMLElement;

    fireEvent.error(videoIn(slide) as HTMLVideoElement);
    await advance(0);
    await waitFor(() => {
      expect(videoIn(slide)).not.toBeNull();
    });
    fireEvent.error(videoIn(slide) as HTMLVideoElement);

    await waitFor(() => {
      expect(videoIn(slide)).toBeNull();
    });
    expect(slide.querySelector('img')).toHaveAttribute('src', proxyUrl(id(1), 'preview'));
  });

  it('plays a Live Photo once, muted, when it appears, and again from its Live button', async () => {
    server.use(onePage(liveMemory(1, at(11, 18)), hidden(2, at(11, 17))));
    await openHidden();
    await openViewer(tileName(1, 'Live Photo'));
    const slide = slides()[0] as HTMLElement;
    expect(videoIn(slide)).toBeNull();

    fireEvent.load(slide.querySelector('img') as HTMLImageElement);
    await advance(0);
    const live = videoIn(slide) as HTMLVideoElement;
    expect(live).toHaveAttribute('src', '/api/v1/proxies/mov1/video?v=1');
    expect(live.muted).toBe(true);
    expect(within(viewer()).queryByRole('button', { name: 'Turn sound on' })).toBeNull();
    fireEvent.ended(live);
    expect(videoIn(slide)).toBeNull();

    await user().click(within(viewer()).getByRole('button', { name: 'Play Live Photo' }));
    expect(videoIn(slide)).not.toBeNull();
    fireEvent.error(videoIn(slide) as HTMLVideoElement);
    expect(videoIn(slide)).toBeNull();
  });

  it('plays a Live Photo again each time it appears', async () => {
    server.use(onePage(liveMemory(1, at(11, 18)), hidden(2, at(11, 17))));
    await openHidden();
    await openViewer(tileName(1, 'Live Photo'));
    const slide = slides()[0] as HTMLElement;
    fireEvent.load(slide.querySelector('img') as HTMLImageElement);
    await advance(0);
    fireEvent.ended(videoIn(slide) as HTMLVideoElement);

    await user().keyboard('{ArrowRight}');
    await user().keyboard('{ArrowLeft}');

    expect(videoIn(slide)).not.toBeNull();
  });
});

describe('unhiding', () => {
  const three = () => [hidden(1, at(11, 18)), hidden(2, at(11, 17)), hidden(3, at(11, 16))];

  it('takes the memory out at once, moves to the next, and offers Undo', async () => {
    server.use(onePage(...three()));
    const { container } = await openHidden();
    await openViewer(tileName(2));

    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));

    expect(caption()).toHaveTextContent('Taken Nov 3, 2019');
    expect(allTiles()).toEqual([tileName(1), tileName(3)]);
    expect(await screen.findByText('Memory unhidden')).toBeVisible();
    await waitFor(() => {
      expect(sent).toEqual([{ id: id(2), body: { hidden: false } }]);
    });
    expect(await axeResults(container.ownerDocument.body)).toHaveNoViolations();
  });

  it('puts the memory back in its place on Undo, hiding it again', async () => {
    server.use(onePage(...three()));
    await openHidden();
    await openViewer(tileName(2));
    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));
    await user().keyboard('{Escape}');

    await user().click(await screen.findByRole('button', { name: 'Undo' }));

    expect(allTiles()).toEqual([tileName(1), tileName(2), tileName(3)]);
    await waitFor(() => {
      expect(sent).toEqual([
        { id: id(2), body: { hidden: false } },
        { id: id(2), body: { hidden: true } },
      ]);
    });
  });

  it('moves to the one before when the last memory is unhidden', async () => {
    server.use(onePage(...three()));
    await openHidden();
    await openViewer(tileName(3));

    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));

    expect(caption()).toHaveTextContent('Taken Nov 2, 2019');
  });

  it('closes the viewer when the only memory left is unhidden', async () => {
    server.use(onePage(hidden(1, at(11, 18))));
    await openHidden();
    await openViewer(tileName(1));

    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByText('Nothing hidden')).toBeVisible();
    await user().click(await screen.findByRole('button', { name: 'Undo' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(allTiles()).toEqual([tileName(1)]);
  });

  it("puts the memory back with Couldn't save when the unhide fails", async () => {
    server.use(
      onePage(...three()),
      interactionAnswers(sent, () => HttpResponse.json({ status: 500 }, { status: 500 })),
    );
    await openHidden();
    await openViewer(tileName(2));

    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));

    expect(await screen.findByText("Couldn't save")).toBeVisible();
    expect(allTiles()).toEqual([tileName(1), tileName(2), tileName(3)]);
    expect(screen.queryByText('Memory unhidden')).toBeNull();
  });

  it("keeps the memory out with Couldn't save when Undo fails", async () => {
    let fails = false;
    server.use(
      onePage(...three()),
      interactionAnswers(sent, () =>
        fails ? HttpResponse.json({ status: 500 }, { status: 500 }) : undefined,
      ),
    );
    await openHidden();
    await openViewer(tileName(2));
    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));
    await waitFor(() => {
      expect(sent).toHaveLength(1);
    });
    fails = true;

    await user().click(await screen.findByRole('button', { name: 'Undo' }));

    expect(await screen.findByText("Couldn't save")).toBeVisible();
    expect(allTiles()).toEqual([tileName(1), tileName(3)]);
  });

  it('marks the days and bursts stale once an unhide or its Undo is saved, so stories reload', async () => {
    const today = '2026-11-18';
    server.use(pages(three()));
    const { router } = renderApp('/hidden');
    await screen.findAllByRole('button', { name: /, taken / });
    const { queryClient } = router.options.context;
    queryClient.setQueryData(['on-this-day', today], { years: [] });
    queryClient.setQueryData(['burst', 'b1'], []);
    const stale = () =>
      [
        ['on-this-day', today],
        ['burst', 'b1'],
      ].map((key) => queryClient.getQueryState(key)?.isInvalidated);
    expect(stale()).toEqual([false, false]);
    await openViewer(tileName(2));

    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));

    await waitFor(() => {
      expect(stale()).toEqual([true, true]);
    });
    await advance(1000);
    expect(asked).toEqual([null]);
    expect(allTiles()).toEqual([tileName(1), tileName(3)]);
  });

  it('leaves the days alone when the unhide is not saved', async () => {
    server.use(
      onePage(...three()),
      interactionAnswers(sent, () => HttpResponse.json({ status: 500 }, { status: 500 })),
    );
    const { router } = renderApp('/hidden');
    await screen.findAllByRole('button', { name: /, taken / });
    const { queryClient } = router.options.context;
    queryClient.setQueryData(['on-this-day', '2026-11-18'], { years: [] });
    await openViewer(tileName(2));

    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));

    expect(await screen.findByText("Couldn't save")).toBeVisible();
    expect(queryClient.getQueryState(['on-this-day', '2026-11-18'])?.isInvalidated).toBe(false);
  });

  it('lists a memory hidden again on top when the screen opens again', async () => {
    let order = three();
    server.use(hiddenAnswers(() => ({ items: order, next: null })));
    const { router } = await openHidden();
    await openViewer(tileName(2));
    await user().click(within(viewer()).getByRole('button', { name: 'Unhide' }));
    await user().keyboard('{Escape}');
    await user().click(await screen.findByRole('button', { name: 'Undo' }));
    expect(allTiles()).toEqual([tileName(1), tileName(2), tileName(3)]);

    order = [hidden(2, at(11, 18, 9)), hidden(1, at(11, 18)), hidden(3, at(11, 16))];
    const { queryClient } = router.options.context;
    queryClient.setQueryData(['on-this-day', '2026-11-18'], { years: [] });
    await act(() => router.navigate({ to: '/settings' }));
    expect(queryClient.getQueryData(['on-this-day', '2026-11-18'])).toEqual({ years: [] });
    await act(() => router.navigate({ to: '/hidden' }));

    await waitFor(() => {
      expect(allTiles()).toEqual([tileName(2), tileName(1), tileName(3)]);
    });
  });
});
