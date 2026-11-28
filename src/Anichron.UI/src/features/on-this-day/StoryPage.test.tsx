import { fireEvent, screen, within, waitFor } from '@testing-library/react';
import { encode } from 'blurhash';
import { http, HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, type MockInstance, vi } from 'vitest';
import type { Memory } from '../../api/memories';
import { axeResults } from '../../test/axe';
import { interactionAnswers, type Interacted } from '../../test/handlers/assets';
import {
  miraSession,
  problemResponse,
  refreshRespondsWith,
  refreshSucceeds,
} from '../../test/handlers/auth';
import {
  hold,
  memory,
  onThisDayAnswers,
  proxyPath,
  proxyUrl,
  recentDaysAnswers,
  recentDaysOf,
  todayAnd,
  yearGroup,
} from '../../test/handlers/flashbacks';
import { burstAnswers } from '../../test/handlers/bursts';
import { holdProxy, proxyAnswers } from '../../test/handlers/proxies';
import { freedSources, keptSource as fromMemoryOf } from '../../test/object-urls';
import { fakeDisplay } from '../../test/display';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { becomes } from '../../test/visibility';
import { advance, user } from '../../test/time';
import { holdPicture } from '../../test/pictures';

const ids = {
  a2025: '0199a1b2-c3d4-7e5f-8a9b-0000000a2025',
  b2025: '0199a1b2-c3d4-7e5f-8a9b-0000000b2025',
  c2025: '0199a1b2-c3d4-7e5f-8a9b-0000000c2025',
  a2023: '0199a1b2-c3d4-7e5f-8a9b-0000000a2023',
  a2021: '0199a1b2-c3d4-7e5f-8a9b-0000000a2021',
  b2021: '0199a1b2-c3d4-7e5f-8a9b-0000000b2021',
};
const preview = (id: string) => proxyUrl(id, 'preview');
const thumbnail = (id: string) => proxyUrl(id, 'thumbnail');
const storyName = 'Memories of November 11, 2026';

const threeYears = () => [
  yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025)),
  yearGroup(2023, memory(ids.a2023, 2023)),
  yearGroup(2021, memory(ids.a2021, 2021), memory(ids.b2021, 2021)),
];

let requested: string[];
let years: ReturnType<typeof threeYears>;

const landingName = 'Wed 11 Nov';
const todayCard = () => screen.getByRole('link', { name: landingName });
const story = () => screen.getByRole('dialog', { name: storyName });
const button = (name: string) => within(story()).getByRole('button', { name });
function fromMemory(source: string | null) {
  const kept = source?.match(/^memory:(.*)$/)?.[1];
  if (kept === undefined) throw new Error(`${String(source)} is not shown from memory`);
  return kept;
}
const showing = () => fromMemory(picture().getAttribute('src'));
async function drag(x: number, y: number, from = middle(), stepMilliseconds = 16) {
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
    await advance(stepMilliseconds);
  }
  return async () => {
    fireEvent.pointerUp(from, at(1));
    await advance(2000);
  };
}

async function swipe(x: number, y: number, from = middle(), stepMilliseconds = 16) {
  const release = await drag(x, y, from, stepMilliseconds);
  await release();
}

const failure = "Couldn't load this memory";
const segment = (index: number) => story().querySelectorAll('ol > li > span')[index] as HTMLElement;
const middle = () => button('Previous').nextElementSibling as HTMLElement;
const fromStore = 'img[src^="memory:"]';
const picture = () => story().querySelector(fromStore) as HTMLImageElement;
const standIn = () => story().querySelector('img:not([src^="memory:"])');

async function shown() {
  await waitFor(() => {
    if (!story().querySelector(fromStore)) waiting.shift()?.();
    expect(story().querySelector(fromStore)).not.toBeNull();
  });
  const image = picture();
  fireEvent.load(image);
  await advance(0);
  return fromMemory(image.getAttribute('src'));
}

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

async function openLanding() {
  const rendered = renderApp('/');
  await screen.findByRole('link', { name: landingName });
  return rendered;
}

async function openLanding2023() {
  const rendered = renderApp('/');
  await screen.findByRole('link', { name: landingName });
  return rendered;
}

const phone = () => fakeDisplay({ jpegXl: true, longEdge: 844 });
const compact = (id: string) => proxyUrl(id, 'compact-preview');
const withCompact = (asset: ReturnType<typeof memory>) => ({
  ...asset,
  proxies: { ...asset.proxies, compactPreview: proxyPath(asset.id, 'compact-preview') },
});

async function openStory(path: string, display = fakeDisplay()) {
  const rendered = renderApp(path, undefined, display);
  await screen.findByRole('dialog', { name: storyName });
  await waitFor(() => {
    expect(picture()).not.toBeNull();
  });
  return rendered;
}

beforeEach(() => {
  waiting = [];
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

let waiting: (() => void)[] = [];

function previewsNotPrefetched() {
  let opened = false;
  server.use(
    proxyAnswers([], (url) => {
      if (!url.includes('/preview?')) return undefined;
      if (!opened) {
        opened = true;
        return undefined;
      }
      return new Promise<undefined>((resolve) => {
        waiting.push(() => {
          resolve(undefined);
        });
      });
    }),
  );
}

describe('opening a story', () => {
  it('opens a dialog at the newest year group', async () => {
    const { router, container } = await openLanding();

    fireEvent.click(todayCard());

    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    expect(router.state.location.href).toBe('/day/2026-11-11?year=2025');
    expect(await shown()).toBe(preview(ids.a2025));
    expect(within(story()).getByText('1 year ago · 2025')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  function holdFirstPreview(received: string[] = [], id = ids.a2025) {
    const { handler, release } = holdProxy(preview(id), received);
    server.use(handler);
    return release;
  }

  it('keeps the tapped year busy until its first picture is loaded when its thumbnail was not decoded', async () => {
    const received: string[] = [];
    const answerFirst = holdFirstPreview(received);
    holdPicture(thumbnail(ids.a2025)).fail();
    await openLanding();
    const tapped = todayCard();

    fireEvent.click(tapped);
    await waitFor(() => {
      expect(received).toContain(preview(ids.a2025));
    });
    await advance(5000);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(todayCard()).toHaveAttribute('aria-busy', 'true');
    expect(received).not.toContain(preview(ids.b2025));
    answerFirst();
    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    expect(showing()).toBe(preview(ids.a2025));
  });

  it('opens at once on the thumbnail the landing screen showed, then on its picture', async () => {
    const answerFirst = holdFirstPreview();
    const { container } = await openLanding();

    fireEvent.click(todayCard());

    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    expect(standIn()).toHaveAttribute('src', thumbnail(ids.a2025));
    expect(picture()).toBeNull();
    await advance(5000);
    expect(segment(0).getAttribute('data-state')).toBe('playing');
    expect(segment(0).style.animationPlayState).toBe('paused');
    expect(await axeResults(container)).toHaveNoViolations();

    answerFirst();
    await waitFor(() => {
      expect(picture()).not.toBeNull();
    });
    expect(standIn()).toHaveAttribute('src', thumbnail(ids.a2025));
    fireEvent.load(picture());
    await advance(0);

    expect(showing()).toBe(preview(ids.a2025));
    expect(standIn()).toBeNull();
    expect(segment(0).style.animationPlayState).toBe('running');
  });

  it('drops the thumbnail when its picture cannot be loaded, and says so', async () => {
    const answerFirst = holdFirstPreview();
    await openLanding();
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });
    expect(standIn()).not.toBeNull();

    await advance(10_000);

    expect(within(story()).getByText(failure)).toBeVisible();
    expect(story().querySelector('img')).toBeNull();
    answerFirst();
  });

  it('keeps a tapped video busy until its preview is loaded, and opens on no thumbnail', async () => {
    years = [
      yearGroup(
        2023,
        memory(ids.a2023, 2023, {
          mediaType: 'Video',
          durationSeconds: 12,
          proxies: {
            thumbnail: proxyPath(ids.a2023, 'thumbnail'),
            preview: proxyPath(ids.a2023, 'preview'),
            compactPreview: null,
            video: proxyPath(ids.a2023, 'video'),
          },
        }),
      ),
    ];
    const answerFirst = holdFirstPreview([], ids.a2023);
    await openLanding2023();

    fireEvent.click(todayCard());
    await advance(5000);

    expect(screen.queryByRole('dialog')).toBeNull();
    answerFirst();
    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    expect(story().querySelector('img')).toBeNull();
  });

  it('loads the first picture only on opening, not again when the session renews', async () => {
    const received: string[] = [];
    server.use(proxyAnswers(received));
    await openLanding();
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });

    await advance(900_000);

    expect(received.filter((url) => url === preview(ids.a2023))).toHaveLength(1);
  });

  it('opens at once on a first memory without a picture, asking for none', async () => {
    years = [
      yearGroup(
        2023,
        memory(ids.a2023, 2023, {
          proxies: { thumbnail: null, preview: null, compactPreview: null, video: null },
        }),
      ),
    ];
    const urls: string[] = [];
    const record = ({ request }: { request: Request }) => {
      urls.push(new URL(request.url).pathname);
    };
    server.events.on('request:start', record);
    await openLanding2023();

    fireEvent.click(todayCard());

    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    server.events.removeListener('request:start', record);
    expect(urls.filter((path) => !path.startsWith('/api/'))).toEqual([]);
  });

  it('opens when the first picture cannot be loaded', async () => {
    server.use(
      proxyAnswers([], (url) =>
        url === preview(ids.a2023) ? new HttpResponse(null, { status: 404 }) : undefined,
      ),
    );
    await openLanding();

    fireEvent.click(todayCard());

    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
  });

  it('opens a reload at the year group in the address, from its first memory', async () => {
    await openStory('/day/2026-11-11?year=2021');

    expect(showing()).toBe(preview(ids.a2021));
    expect(within(story()).getByText('5 years ago · 2021')).toBeVisible();
  });

  it.each(['/day/2026-11-11?year=2024', '/day/2026-11-11?year=soon', '/day/2026-11-11'])(
    'opens %s at the first year group',
    async (path) => {
      await openStory(path);

      expect(showing()).toBe(preview(ids.a2025));
    },
  );

  it('counts the years from the day of the story', async () => {
    renderApp('/day/2024-11-11?year=2023');

    expect(
      await screen.findByRole('dialog', { name: 'Memories of November 11, 2024' }),
    ).toBeVisible();
    expect(await screen.findByText('1 year ago · 2023')).toBeVisible();
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
    expect(await shown()).toBe(preview(ids.a2023));
  });

  it('closes at once on a day already loaded without memories', async () => {
    years = [];
    const { router } = renderApp('/');
    await screen.findByText('Nothing from this day in earlier years');

    await act(() => router.navigate({ href: '/day/2026-11-11?year=2023' }));

    expect(await screen.findByText('Nothing from this day in earlier years')).toBeVisible();
    expect(router.state.location.href).toBe('/');
    expect(screen.queryByRole('dialog')).toBeNull();
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
    await advance(9000);
    expect(await shown()).toBe(preview(ids.a2025));
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('shows a memory without a preview for 5 s from the moment it is on screen', async () => {
    previewsNotPrefetched();
    const bare = (id: string) =>
      memory(id, 2025, {
        proxies: { thumbnail: null, preview: null, compactPreview: null, video: null },
      });
    years = [
      yearGroup(2025, bare(ids.a2025), memory(ids.b2025, 2025)),
      yearGroup(2023, bare(ids.a2023)),
      yearGroup(2021, memory(ids.a2021, 2021)),
    ];
    renderApp('/day/2026-11-11?year=2025');
    await screen.findByRole('dialog', { name: storyName });
    await waitFor(() => {
      expect(story().querySelectorAll('ol > li')).toHaveLength(2);
    });
    expect(picture()).toBeNull();

    await advance(5000);
    expect(await shown()).toBe(preview(ids.b2025));
    await advance(5000);
    expect(picture()).toBeNull();
    expect(within(story()).getByText('3 years ago · 2023')).toBeVisible();
    await advance(4999);
    expect(picture()).toBeNull();
    await advance(1);
    expect(await shown()).toBe(preview(ids.a2021));
  });

  it('counts a picture that loads but fails to decode as shown', async () => {
    await openStory('/day/2026-11-11?year=2025');
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockRejectedValue(new Error('EncodingError'));

    await shown();
    await advance(5000);

    expect(showing()).toBe(preview(ids.b2025));
  });

  it('plays year group after year group, and rests on the last memory until moved on', async () => {
    const { router } = await openLanding();
    fireEvent.click(todayCard());
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
    await advance(60_000);
    expect(showing()).toBe(preview(ids.b2021));
    expect(segment(1).getAttribute('data-state')).toBe('seen');

    await user().click(button('Next'));
    await advance(0);
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
    expect(router.history.canGoBack()).toBe(false);
    expect(todayCard()).toBeVisible();
  });

  it('rests on the last memory of a reloaded story too', async () => {
    await openStory('/day/2026-11-11?year=2021');
    await shown();
    await advance(5000);
    await shown();
    await advance(60_000);

    expect(story()).toBeVisible();
    expect(showing()).toBe(preview(ids.b2021));
  });

  it('rests on the last memory when played again with the pause button', async () => {
    await openStory('/day/2026-11-11?year=2021');
    await shown();
    await advance(5000);
    await shown();
    await advance(5000);

    await user().click(button('Pause'));
    await user().click(button('Play'));
    await advance(60_000);

    expect(showing()).toBe(preview(ids.b2021));
    expect(segment(1).getAttribute('data-state')).toBe('seen');
  });

  it('rests with the one segment of a year group of one memory full', async () => {
    years = [yearGroup(2023, memory(ids.a2023, 2023))];
    await openStory('/day/2026-11-11?year=2023');
    await shown();
    await advance(5000);

    expect(showing()).toBe(preview(ids.a2023));
    expect(segment(0).getAttribute('data-state')).toBe('seen');
  });

  it('keeps a paused segment where it stopped', async () => {
    await openStory('/day/2026-11-11?year=2021');
    await shown();
    await user().click(button('Pause'));

    expect(segment(0).getAttribute('data-state')).toBe('playing');
    expect(segment(0)).toHaveStyle({ animationPlayState: 'paused' });
  });

  it('plays again from the last memory when going back', async () => {
    await openStory('/day/2026-11-11?year=2021');
    await shown();
    await advance(5000);
    await shown();
    await advance(5000);

    await user().click(button('Previous'));
    await shown();
    await advance(5000);

    expect(await shown()).toBe(preview(ids.b2021));
    expect(segment(1)).toHaveStyle({ animationPlayState: 'running' });
  });

  it('holds the progress of a memory still until its picture is decoded', async () => {
    previewsNotPrefetched();
    await openStory('/day/2026-11-11?year=2025');

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
    expect(await shown()).toBe(preview(ids.b2025));
    await advance(5000);
    expect(showing()).toBe(preview(ids.a2023));
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

describe('a memory loading', () => {
  beforeEach(() => {
    previewsNotPrefetched();
  });

  async function loadingSecond() {
    const rendered = await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Next'));
    return rendered;
  }

  it('fails a picture that has not arrived after 10 s, and moves on 5 s later', async () => {
    const { container } = await loadingSecond();

    await advance(9999);
    expect(within(story()).queryByText(failure)).toBeNull();
    await advance(1);
    expect(within(story()).getByText(failure)).toBeVisible();
    expect(picture()).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('shows a failed memory for 5 s before moving on', async () => {
    await loadingSecond();
    await advance(10_000);

    await advance(4999);
    expect(within(story()).getByText(failure)).toBeVisible();
    await advance(1);
    expect(within(story()).queryByText(failure)).toBeNull();
    expect(await shown()).toBe(preview(ids.a2023));
  });

  it('fails a picture the server refuses at once, and moves on 5 s later', async () => {
    server.use(
      proxyAnswers([], (url) =>
        url === preview(ids.b2025) ? new HttpResponse(null, { status: 503 }) : undefined,
      ),
    );
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Next'));
    await advance(0);

    expect(within(story()).getByText(failure)).toBeVisible();
    expect(picture()).toBeNull();
    await advance(4999);
    expect(within(story()).getByText(failure)).toBeVisible();
    await advance(1);
    expect(await shown()).toBe(preview(ids.a2023));
  });

  it('downloads a failed picture again when the story comes back to it', async () => {
    let refusing = true;
    server.use(
      proxyAnswers([], (url) =>
        url === preview(ids.b2025) && refusing
          ? new HttpResponse(null, { status: 503 })
          : undefined,
      ),
    );
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Next'));
    await advance(0);
    expect(within(story()).getByText(failure)).toBeVisible();

    refusing = false;
    await user().click(button('Previous'));
    await user().click(button('Next'));

    expect(within(story()).queryByText(failure)).toBeNull();
    expect(await shown()).toBe(preview(ids.b2025));
  });

  it('fails a picture that breaks, without renewing the session, and moves on 5 s later', async () => {
    let refreshes = 0;
    let answeredAt = 0;
    server.use(
      refreshRespondsWith(() => {
        refreshes++;
        answeredAt = Date.now();
        return HttpResponse.json(miraSession);
      }),
    );
    await loadingSecond();
    await shown();
    vi.setSystemTime(answeredAt + miraSession.expiresIn * 800 + 1);

    fireEvent.error(picture());
    await advance(0);

    expect(refreshes).toBe(1);
    expect(within(story()).getByText(failure)).toBeVisible();
    expect(picture()).toBeNull();
    await advance(4999);
    expect(within(story()).getByText(failure)).toBeVisible();
    await advance(1);
    expect(await shown()).toBe(preview(ids.a2023));
  });

  it('stays failed when its picture arrives too late', async () => {
    await loadingSecond();

    await advance(10_000);
    waiting.shift()?.();
    await advance(4999);

    expect(within(story()).getByText(failure)).toBeVisible();
    expect(picture()).toBeNull();
    await advance(1);
    expect(await shown()).toBe(preview(ids.a2023));
  });

  function trickling(url: string) {
    let stream: ReadableStreamDefaultController<Uint8Array> | undefined;
    server.use(
      http.get('/api/v1/proxies/:id/:type', ({ request }) => {
        const { pathname, search } = new URL(request.url);
        if (pathname + search !== url) return undefined;
        return new HttpResponse(
          new ReadableStream<Uint8Array>({
            start(controller) {
              stream = controller;
            },
          }),
        );
      }),
    );
    return {
      async piece() {
        stream?.enqueue(new Uint8Array([1]));
        await advance(0);
      },
      async end() {
        stream?.close();
        await advance(0);
      },
    };
  }

  it('waits for a picture as long as more of it keeps arriving', async () => {
    const download = trickling(preview(ids.b2025));
    await loadingSecond();

    for (let piece = 0; piece < 5; piece++) {
      await advance(9000);
      await download.piece();
    }
    expect(within(story()).queryByText(failure)).toBeNull();
    await download.end();

    expect(await shown()).toBe(preview(ids.b2025));
  });

  it('fails a picture 10 s after the last of it arrived', async () => {
    const download = trickling(preview(ids.b2025));
    await loadingSecond();
    await advance(9000);
    await download.piece();

    await advance(9999);
    expect(within(story()).queryByText(failure)).toBeNull();
    await advance(1);
    expect(within(story()).getByText(failure)).toBeVisible();
  });

  it('downloads the picture showing ahead of one still arriving', async () => {
    trickling(preview(ids.a2021));
    await openStory('/day/2026-11-11?year=2023');
    await shown();

    await user().click(button('Previous'));

    expect(await shown()).toBe(preview(ids.b2025));
  });

  it('renews the session once on a preview refused past its renewal, and downloads it again', async () => {
    let refreshes = 0;
    let answeredAt = 0;
    let release: () => void = () => undefined;
    const refusing = new Promise<void>((resolve) => {
      release = resolve;
    });
    let asked = 0;
    server.use(
      refreshRespondsWith(() => {
        refreshes++;
        answeredAt = Date.now();
        return HttpResponse.json(miraSession);
      }),
      proxyAnswers([], async (url) => {
        if (url !== preview(ids.b2025) || ++asked > 1) return undefined;
        await refusing;
        return problemResponse(401, 'unauthorized');
      }),
    );
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    vi.setSystemTime(answeredAt + miraSession.expiresIn * 800 + 1);

    release();
    await user().click(button('Next'));

    expect(await shown()).toBe(preview(ids.b2025));
    expect(refreshes).toBe(2);
    expect(asked).toBe(2);
  });

  it('gives each memory its own 10 s to arrive', async () => {
    await openStory('/day/2026-11-11?year=2025');

    await user().click(button('Next'));
    await advance(9999);

    expect(within(story()).queryByText(failure)).toBeNull();
    await advance(1);
    expect(within(story()).getByText(failure)).toBeVisible();
  });

  it('stops the 10 s to arrive while paused, and keeps the time left', async () => {
    await loadingSecond();

    await advance(4000);
    await user().click(button('Pause'));
    await advance(30_000);
    expect(within(story()).queryByText(failure)).toBeNull();
    await user().click(button('Play'));
    await advance(5999);
    expect(within(story()).queryByText(failure)).toBeNull();
    await advance(1);
    expect(within(story()).getByText(failure)).toBeVisible();
  });

  it('stops the 10 s to arrive while held', async () => {
    await loadingSecond();

    await advance(3500);
    const pointer = user();
    await pointer.pointer({ keys: '[MouseLeft>]', target: middle() });
    await advance(30_000);
    expect(within(story()).queryByText(failure)).toBeNull();
    await pointer.pointer({ keys: '[/MouseLeft]', target: middle() });
    await advance(5999);
    expect(within(story()).queryByText(failure)).toBeNull();
    await advance(1);
    expect(within(story()).getByText(failure)).toBeVisible();
  });

  it('fills the progress segment while a failed memory shows', async () => {
    await loadingSecond();
    expect(segment(1).style.animationPlayState).toBe('paused');

    await advance(10_000);

    expect(segment(1).getAttribute('data-state')).toBe('playing');
    expect(segment(1).style.animationPlayState).toBe('running');
  });

  it('stops waiting for a picture once closed', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    await shown();
    await user().click(button('Next'));

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);
    const closed = router.history.location.state.__TSR_key;
    await advance(15_000);

    expect(router.history.location.state.__TSR_key).toBe(closed);
  });
});

describe('a video', () => {
  const clip = (id: string) => proxyUrl(id, 'video');
  const video = () => story().querySelector('video') as HTMLVideoElement;
  const film = (id: string, year: number, overrides: Parameters<typeof memory>[2] = {}) =>
    memory(id, year, {
      mediaType: 'Video',
      durationSeconds: 12,
      proxies: {
        thumbnail: proxyPath(id, 'thumbnail'),
        preview: proxyPath(id, 'preview'),
        compactPreview: null,
        video: proxyPath(id, 'video'),
      },
      ...overrides,
    });

  async function openVideo(path = '/day/2026-11-11?year=2025') {
    const rendered = renderApp(path);
    await screen.findByRole('dialog', { name: storyName });
    await waitFor(() => {
      expect(story().querySelector('video')).not.toBeNull();
    });
    return rendered;
  }

  async function fire(event: 'playing' | 'ended' | 'error') {
    fireEvent[event](video());
    await advance(0);
  }

  beforeEach(() => {
    years = [
      yearGroup(2025, film(ids.a2025, 2025), memory(ids.b2025, 2025), film(ids.c2025, 2025)),
      yearGroup(2023, memory(ids.a2023, 2023)),
    ];
  });

  it('plays in the story, muted and inline, until it ends', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
    const { container } = await openVideo();

    expect(video().getAttribute('src')).toBe(clip(ids.a2025));
    expect(video().muted).toBe(true);
    expect(video().playsInline).toBe(true);
    expect(play).toHaveBeenCalled();
    await fire('playing');
    await advance(30_000);
    expect(video()).toBeVisible();
    await fire('ended');

    expect(showing()).toBe(preview(ids.b2025));
    expect(video()).not.toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('rests on its end when it is the last memory', async () => {
    years = [yearGroup(2025, film(ids.a2025, 2025))];
    await openVideo();
    await fire('playing');

    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
    await fire('ended');
    await advance(60_000);
    const pointer = user();
    await pointer.pointer({ keys: '[MouseLeft>]', target: middle() });
    await advance(1000);
    await pointer.pointer({ keys: '[/MouseLeft]', target: middle() });
    await user().click(button('Pause'));
    await user().click(button('Play'));

    expect(play).not.toHaveBeenCalled();
    expect(video()).toBeVisible();
    expect(segment(0)).toHaveStyle({ animationPlayState: 'paused' });
  });

  it('loads its video once while it plays', async () => {
    await openVideo();
    const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');

    await fire('playing');
    await advance(5000);

    expect(src).not.toHaveBeenCalled();
  });

  it('shows its preview from memory as its poster, once downloaded', async () => {
    await openVideo();

    await waitFor(() => {
      expect(video().getAttribute('poster')).toBe(fromMemoryOf(preview(ids.a2025)));
    });
  });

  it('stops waiting for it 10 s after it started, however much of its poster arrives', async () => {
    let stream: ReadableStreamDefaultController<Uint8Array> | undefined;
    server.use(
      http.get('/api/v1/proxies/:id/:type', ({ request }) => {
        const { pathname, search } = new URL(request.url);
        if (pathname + search !== preview(ids.a2025)) return undefined;
        return new HttpResponse(
          new ReadableStream<Uint8Array>({
            start(controller) {
              stream = controller;
            },
          }),
        );
      }),
    );
    await openVideo();

    await advance(9000);
    stream?.enqueue(new Uint8Array([1]));
    await advance(1000);

    expect(video()).not.toBeVisible();
    expect(segment(0).style.animationDuration).toBe('5000ms');
  });

  it('plays when its poster cannot be downloaded', async () => {
    server.use(
      proxyAnswers([], (url) =>
        url === preview(ids.a2025) ? new HttpResponse(null, { status: 404 }) : undefined,
      ),
    );
    await openVideo();
    await advance(1000);

    await fire('playing');

    expect(video()).toBeVisible();
    expect(video().getAttribute('poster')).toBe('');
    expect(within(story()).queryByText(failure)).toBeNull();
  });

  it('has no poster without a preview', async () => {
    years = [
      yearGroup(
        2025,
        film(ids.a2025, 2025, {
          proxies: {
            thumbnail: null,
            preview: null,
            compactPreview: null,
            video: proxyPath(ids.a2025, 'video'),
          },
        }),
      ),
    ];
    await openVideo();

    expect(video().getAttribute('poster')).toBe('');
  });

  it('lasts at most 120 s from the moment it plays', async () => {
    await openVideo();
    await advance(9000);
    await fire('playing');

    await advance(119_999);
    expect(video()).toBeVisible();
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('keeps its clock when it plays again after a pause', async () => {
    await openVideo();
    await fire('playing');
    await advance(20_000);
    await user().click(button('Pause'));
    await user().click(button('Play'));
    await fire('playing');

    await advance(99_999);
    expect(video()).toBeVisible();
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('pauses with the story and plays on when it plays', async () => {
    await openVideo();
    await fire('playing');
    const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause');
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

    await user().click(button('Pause'));
    expect(pause).toHaveBeenCalled();
    await advance(120_000);
    expect(play).not.toHaveBeenCalled();
    expect(video()).toBeVisible();

    await user().click(button('Play'));
    expect(play).toHaveBeenCalled();
  });

  it('tries to play again on the next change when the browser refused to play it', async () => {
    const play = vi
      .spyOn(HTMLMediaElement.prototype, 'play')
      .mockRejectedValue(new DOMException('Low Power Mode', 'NotAllowedError'));
    await openVideo();
    await advance(0);
    play.mockClear();

    await user().keyboard('m');

    expect(play).toHaveBeenCalled();
  });

  it('pauses while held', async () => {
    await openVideo();
    await fire('playing');
    const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause');
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
    const pointer = user();

    await pointer.pointer({ keys: '[MouseLeft>]', target: button('Next') });
    await advance(120_000);
    expect(pause).toHaveBeenCalled();
    expect(video()).toBeVisible();
    expect(play).not.toHaveBeenCalled();

    await pointer.pointer({ keys: '[/MouseLeft]', target: button('Next') });
    expect(play).toHaveBeenCalled();
  });

  it('holds its progress segment still until it plays, then fills it over its length', async () => {
    await openVideo();
    expect(segment(0).style.animationPlayState).toBe('paused');

    await fire('playing');

    expect(segment(0).style.animationPlayState).toBe('running');
    expect(segment(0).style.animationDuration).toBe('12000ms');
  });

  it.each([
    [90, '90000ms'],
    [150, '120000ms'],
    [null, '120000ms'],
  ])(
    'fills the progress segment of a video of %s s over at most 120 s',
    async (durationSeconds, filling) => {
      years = [yearGroup(2025, film(ids.a2025, 2025, { durationSeconds }))];
      await openVideo();
      await fire('playing');

      expect(segment(0).style.animationDuration).toBe(filling);
    },
  );

  it('fills the progress segment of a photo over 5 s', async () => {
    await openVideo();
    await fire('ended');

    expect(segment(1).style.animationDuration).toBe('5000ms');
  });

  it('shows its poster for 5 s when it breaks again after its reload, then moves on', async () => {
    await openVideo();
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
    const first = video();

    await fire('error');
    expect(video()).toBe(first);
    expect(play).toHaveBeenCalled();
    expect(picture()).toBeNull();
    await fire('error');

    expect(video()).not.toBeVisible();
    expect(video().hasAttribute('src')).toBe(false);
    expect(showing()).toBe(preview(ids.a2025));
    expect(within(story()).queryByText(failure)).toBeNull();
    expect(segment(0).style.animationDuration).toBe('5000ms');
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('shows its poster for 5 s when it breaks while playing and again after its reload', async () => {
    await openVideo();
    await fire('playing');

    await fire('error');
    await fire('error');

    expect(video()).not.toBeVisible();
    expect(showing()).toBe(preview(ids.a2025));
    expect(segment(0).style.animationDuration).toBe('5000ms');
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('plays after its reload', async () => {
    await openVideo();

    await fire('error');
    await fire('playing');
    await advance(119_999);

    expect(video()).toBeVisible();
  });

  it('reloads the same video once, and plays it', async () => {
    await openVideo();
    await advance(1000);
    const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

    await fire('error');

    expect(src.mock.calls).toEqual([[clip(ids.a2025)]]);
    expect(play).toHaveBeenCalledOnce();
  });

  it('shows its poster when it has not played after 10 s', async () => {
    await openVideo();
    await user().click(button('Previous'));

    await advance(9999);
    expect(picture()).toBeNull();
    await advance(1);

    expect(video()).not.toBeVisible();
    expect(showing()).toBe(preview(ids.a2025));
  });

  it('ignores a video that ends or breaks after the story moved on', async () => {
    previewsNotPrefetched();
    await openVideo();
    const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');
    await fire('error');
    await user().click(button('Next'));
    await shown();

    await fire('ended');
    await fire('error');
    await fire('playing');

    expect(showing()).toBe(preview(ids.b2025));
    expect(src).toHaveBeenCalledTimes(1);
    await advance(4999);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('does not reload a video the story moved on from while its session renewed', async () => {
    await openVideo();
    fireEvent.error(video());
    const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');
    fireEvent.click(button('Next'));
    await advance(0);

    expect(src).not.toHaveBeenCalled();
  });

  it('plays every video in one element, and lets go of the one it left', async () => {
    await openVideo();
    const shared = video();
    const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause');
    const load = vi.spyOn(HTMLMediaElement.prototype, 'load');
    await fire('playing');

    await fire('ended');
    expect(pause).toHaveBeenCalled();
    expect(load).toHaveBeenCalledOnce();
    expect(video().hasAttribute('src')).toBe(false);
    await shown();
    await user().click(button('Pause'));
    await user().click(button('Play'));
    expect(load).toHaveBeenCalledOnce();
    await advance(5000);

    expect(video()).toBe(shared);
    expect(video()).toBeVisible();
    expect(video().getAttribute('src')).toBe(clip(ids.c2025));
    expect(load).toHaveBeenCalledOnce();
  });

  it('waits for a video to play again when returning to it', async () => {
    await openVideo();
    await fire('playing');
    await advance(1000);
    const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');

    await user().click(button('Previous'));
    expect(src).toHaveBeenCalledWith(clip(ids.a2025));
    expect(segment(0).style.animationPlayState).toBe('paused');
    await advance(9999);
    expect(video()).toBeVisible();
    await advance(1);
    expect(video()).not.toBeVisible();
  });

  it('pauses its video while the story is held', async () => {
    await openVideo();
    await fire('playing');
    const pause = vi.spyOn(HTMLMediaElement.prototype, 'pause');
    const pointer = user();

    await pointer.pointer({ keys: '[MouseLeft>]', target: button('Next') });
    await advance(1000);

    expect(pause).toHaveBeenCalled();
    await pointer.pointer({ keys: '[/MouseLeft]', target: button('Next') });
  });

  describe('its sound', () => {
    const sound = (name: string) => within(story()).getByRole('button', { name });

    it('is off at first, and the speaker button turns it on and off', async () => {
      const { container } = await openVideo();
      await advance(2000);

      await user().click(sound('Turn sound on'));
      expect(video().muted).toBe(false);
      expect(await axeResults(container)).toHaveNoViolations();
      await user().click(sound('Turn sound off'));
      expect(video().muted).toBe(true);
      expect(video()).toBeVisible();
    });

    it('stays on for the next video', async () => {
      await openVideo();
      await fire('playing');
      await advance(1000);
      await user().click(sound('Turn sound on'));
      await fire('ended');
      await shown();
      await advance(5000);

      expect(video().getAttribute('src')).toBe(clip(ids.c2025));
      expect(video().muted).toBe(false);
      expect(sound('Turn sound off')).toBeVisible();
    });

    it('turns on and off from a tap in the middle third, without moving on', async () => {
      await openVideo();
      await fire('playing');

      await user().click(middle());
      expect(video().muted).toBe(false);
      await user().click(middle());
      expect(video().muted).toBe(true);
      expect(video()).toBeVisible();
    });

    it('turns on and off with M', async () => {
      await openVideo();

      await user().keyboard('m');
      expect(video().muted).toBe(false);
      await user().keyboard('m');
      expect(video().muted).toBe(true);
    });

    it('turns on and off with M under Caps Lock or Shift', async () => {
      await openVideo();

      await user().keyboard('M');
      expect(video().muted).toBe(false);
      await user().keyboard('{Shift>}M{/Shift}');
      expect(video().muted).toBe(true);
    });

    it('stays as it is on a photo, where M does nothing and the middle third moves on', async () => {
      await openVideo();
      await fire('ended');
      await shown();

      await user().keyboard('m');
      expect(within(story()).queryByRole('button', { name: /^Turn sound/ })).toBeNull();
      await user().click(middle());

      expect(video().getAttribute('src')).toBe(clip(ids.c2025));
      expect(video().muted).toBe(true);
    });

    it('hides its button while the story is held', async () => {
      await openVideo();
      await fire('playing');
      await advance(1000);
      const pointer = user();

      await pointer.pointer({ keys: '[MouseLeft>]', target: button('Next') });
      await advance(500);

      expect(within(story()).queryByRole('button', { name: 'Turn sound on' })).toBeNull();
    });
  });

  describe('the first one', () => {
    const hint = () => story().querySelector('[data-hint]') as HTMLElement;

    it('shows where the sound is in the middle until 1 s after it plays, then in the corner', async () => {
      const { container } = await openVideo();

      expect(hint().getAttribute('data-hint')).toBe('true');
      expect(within(hint()).getByRole('button', { name: 'Turn sound on' })).toBeVisible();
      expect(await axeResults(container)).toHaveNoViolations();
      await advance(5000);
      expect(hint().getAttribute('data-hint')).toBe('true');
      await fire('playing');
      await advance(999);
      expect(hint().getAttribute('data-hint')).toBe('true');
      await advance(1);
      expect(hint().getAttribute('data-hint')).toBe('false');
      expect(within(hint()).getByRole('button', { name: 'Turn sound on' })).toBeVisible();
    });

    it('keeps showing in the middle while the story is paused', async () => {
      await openVideo();
      await fire('playing');
      await advance(400);

      await user().click(button('Pause'));
      await advance(30_000);
      expect(hint().getAttribute('data-hint')).toBe('true');
      await user().click(button('Play'));
      await advance(599);
      expect(hint().getAttribute('data-hint')).toBe('true');
      await advance(1);
      expect(hint().getAttribute('data-hint')).toBe('false');
    });

    it('shrinks into the corner 1 s after its video fails', async () => {
      await openVideo();
      await fire('error');
      await fire('error');

      await advance(999);
      expect(hint().getAttribute('data-hint')).toBe('true');
      await advance(1);
      expect(hint().getAttribute('data-hint')).toBe('false');
    });

    it('turns the sound on when tapped', async () => {
      await openVideo();

      await user().click(within(hint()).getByRole('button'));

      expect(video().muted).toBe(false);
      expect(hint().getAttribute('data-hint')).toBe('false');
    });

    it('is the only one shown where the sound is', async () => {
      await openVideo();
      await fire('playing');
      await advance(1000);
      await fire('ended');
      await shown();
      await advance(5000);

      expect(hint().getAttribute('data-hint')).toBe('false');
    });

    it('is not shown again when returning to it', async () => {
      await openVideo();
      await fire('playing');
      await advance(1000);

      await user().click(button('Previous'));

      expect(hint().getAttribute('data-hint')).toBe('false');
    });

    it('may come after photos', async () => {
      years = [yearGroup(2025, memory(ids.a2025, 2025), film(ids.b2025, 2025))];
      renderApp('/day/2026-11-11?year=2025');
      await within(await screen.findByRole('dialog', { name: storyName })).findByRole('button', {
        name: 'Previous',
      });
      await waitFor(() => {
        expect(picture()).not.toBeNull();
      });
      expect(hint()).toBeNull();
      await shown();
      await advance(5000);

      expect(hint().getAttribute('data-hint')).toBe('true');
    });

    it('waits for the first video past several photos', async () => {
      years = [
        yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025), film(ids.c2025, 2025)),
      ];
      renderApp('/day/2026-11-11?year=2025');
      await within(await screen.findByRole('dialog', { name: storyName })).findByRole('button', {
        name: 'Previous',
      });
      await waitFor(() => {
        expect(picture()).not.toBeNull();
      });
      await user().click(button('Next'));
      await user().click(button('Next'));

      expect(hint().getAttribute('data-hint')).toBe('true');
    });

    it('stops showing once the story moves on', async () => {
      years = [
        yearGroup(2025, film(ids.a2025, 2025), film(ids.b2025, 2025)),
        yearGroup(2023, memory(ids.a2023, 2023)),
      ];
      await openVideo();
      await user().click(button('Next'));

      expect(hint().getAttribute('data-hint')).toBe('false');
    });
  });
});

describe('a Live Photo', () => {
  const movId = '0199a1b2-c3d4-7e5f-8a9b-0000000d2025';
  const keptSource = `memory:${proxyUrl(movId, 'video')}`;
  const video = () => story().querySelector('video') as HTMLVideoElement;
  const live = (id: string, year: number) =>
    memory(id, year, {
      mediaType: 'LivePhoto',
      livePhotoVideoUrl: proxyPath(movId, 'video'),
    });

  async function fire(event: 'playing' | 'ended' | 'error') {
    fireEvent[event](video());
    await advance(0);
  }

  beforeEach(() => {
    years = [yearGroup(2025, live(ids.a2025, 2025), memory(ids.b2025, 2025))];
  });

  it('shows its photo for 5 s and plays its video only when asked', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();

    await advance(4999);
    expect(play).not.toHaveBeenCalled();
    expect(video().hasAttribute('src')).toBe(false);
    expect(video()).not.toBeVisible();
    expect(showing()).toBe(preview(ids.a2025));
    expect(await axeResults(container)).toHaveNoViolations();
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('leaves the photo on screen when its video breaks, without reloading it', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(1000);
    const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');
    await user().click(button('Play Live Photo'));
    await fire('playing');

    await fire('error');
    expect(video()).not.toBeVisible();
    expect(src).toHaveBeenCalledOnce();
    expect(showing()).toBe(preview(ids.a2025));
    await advance(3999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('plays nothing when its photo fails', async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
    await openStory('/day/2026-11-11?year=2025');

    await advance(10_000);

    expect(within(story()).getByText(failure)).toBeVisible();
    expect(play).not.toHaveBeenCalled();
    expect(within(story()).queryByRole('button', { name: 'Play Live Photo' })).toBeNull();
  });

  it('stays muted after the sound was turned on for a video', async () => {
    years = [
      yearGroup(
        2025,
        memory(ids.c2025, 2025, {
          mediaType: 'Video',
          durationSeconds: 3,
          proxies: {
            thumbnail: null,
            preview: null,
            compactPreview: null,
            video: `proxies/${ids.c2025}/video?v=0`,
          },
        }),
        live(ids.a2025, 2025),
      ),
    ];
    renderApp('/day/2026-11-11?year=2025');
    await user().click(
      await within(await screen.findByRole('dialog')).findByRole('button', {
        name: 'Turn sound on',
      }),
    );
    expect(video().muted).toBe(false);

    await fire('ended');
    await shown();
    await user().click(button('Play Live Photo'));

    expect(video().getAttribute('src')).toBe(keptSource);
    expect(video().muted).toBe(true);
  });

  it('moves to the next memory from a tap in the middle', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().click(middle());

    expect(showing()).toBe(preview(ids.b2025));
  });

  it('pauses while held before its photo shows, playing nothing, and plays on when released', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const pointer = user();
    await pointer.pointer({ keys: '[MouseLeft>]', target: middle() });
    await advance(1000);
    expect(video().hasAttribute('src')).toBe(false);
    const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

    await shown();
    await advance(30_000);
    expect(play).not.toHaveBeenCalled();
    expect(button('Play Live Photo')).toHaveAttribute('data-live', 'still');
    expect(showing()).toBe(preview(ids.a2025));

    await pointer.pointer({ keys: '[/MouseLeft]', target: middle() });
    expect(play).not.toHaveBeenCalled();
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  describe('held', () => {
    async function hold() {
      const pointer = user();
      await pointer.pointer({ keys: '[MouseLeft>]', target: middle() });
      await advance(1000);
      return async () => {
        await pointer.pointer({ keys: '[/MouseLeft]', target: middle() });
      };
    }

    it('plays its video once more from the start, and the story holds still', async () => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      const play = vi.spyOn(HTMLMediaElement.prototype, 'play');
      const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');

      const release = await hold();
      expect(src).toHaveBeenCalledExactlyOnceWith(keptSource);
      expect(play).toHaveBeenCalledOnce();
      expect(video().muted).toBe(true);
      await fire('playing');
      expect(video()).toBeVisible();
      await advance(30_000);
      expect(showing()).toBe(preview(ids.a2025));
      await fire('ended');
      expect(video()).not.toBeVisible();
      expect(showing()).toBe(preview(ids.a2025));
      await release();

      expect(segment(0).style.animationPlayState).toBe('running');
    });

    it('starts its video again when held during a play', async () => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await user().click(button('Play Live Photo'));
      await fire('playing');
      const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');

      const release = await hold();

      expect(src).toHaveBeenCalledExactlyOnceWith(keptSource);
      await release();
    });

    it('stops its video when released, and the story plays on', async () => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      const release = await hold();
      await fire('playing');

      await release();

      expect(video()).not.toBeVisible();
      expect(video().hasAttribute('src')).toBe(false);
      expect(segment(0).style.animationPlayState).toBe('running');
      await advance(5000);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('plays its video while the story is paused', async () => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await user().click(button('Pause'));
      const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

      const release = await hold();

      expect(play).toHaveBeenCalledOnce();
      await release();
      expect(video().hasAttribute('src')).toBe(false);
      expect(within(story()).getByRole('button', { name: 'Play' })).toBeVisible();
    });

    it('keeps its Live pill showing what its video does, and hides the other controls', async () => {
      const live = () => button('Play Live Photo').getAttribute('data-live');
      await openStory('/day/2026-11-11?year=2025');
      await shown();

      const release = await hold();

      expect(button('Play Live Photo')).toBeVisible();
      expect(live()).toBe('loading');
      await fire('playing');
      expect(live()).toBe('playing');
      await fire('ended');
      expect(live()).toBe('still');
      expect(button('Play Live Photo')).toBeVisible();
      expect(within(story()).queryByRole('button', { name: 'Pause' })).toBeNull();
      expect(within(story()).queryByRole('button', { name: 'Star' })).toBeNull();
      expect(within(story()).getByText('1 year ago · 2025')).toBeVisible();
      await release();
    });

    it.each([
      ['photo', memory(ids.a2025, 2025)],
      [
        'video',
        memory(ids.a2025, 2025, {
          mediaType: 'Video',
          proxies: {
            thumbnail: null,
            preview: `proxies/${ids.a2025}/preview?v=1`,
            compactPreview: null,
            video: `proxies/${ids.a2025}/video?v=1`,
          },
        }),
      ],
      [
        'burst',
        memory(ids.a2025, 2025, {
          burst: { burstId: ids.c2025, isCover: true, count: 2 },
          livePhotoVideoUrl: proxyPath(movId, 'video'),
        }),
      ],
    ])('shows no Live pill on a hold on a %s', async (_, held) => {
      years = [yearGroup(2025, held)];
      renderApp('/day/2026-11-11?year=2025');
      await within(await screen.findByRole('dialog', { name: storyName })).findByRole('button', {
        name: 'Previous',
      });

      const release = await hold();

      expect(within(story()).queryByRole('button', { name: 'Play Live Photo' })).toBeNull();
      expect(within(story()).queryByRole('button', { name: 'Pause' })).toBeNull();
      await release();
    });

    it('plays its video when the story rests on it at the end', async () => {
      years = [yearGroup(2025, live(ids.a2025, 2025))];
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await advance(5000);
      const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

      const release = await hold();

      expect(video().getAttribute('src')).toBe(keptSource);
      expect(play).toHaveBeenCalledOnce();
      await release();
    });
  });

  it('shows no Live Photo button on a photo, nor before its photo shows', async () => {
    previewsNotPrefetched();
    years = [yearGroup(2025, memory(ids.b2025, 2025), live(ids.a2025, 2025))];
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    expect(within(story()).queryByRole('button', { name: 'Play Live Photo' })).toBeNull();

    await user().click(button('Next'));
    expect(within(story()).queryByRole('button', { name: 'Play Live Photo' })).toBeNull();
    await shown();

    expect(button('Play Live Photo')).toBeVisible();
  });

  describe('its Live Photo button', () => {
    async function replay() {
      await user().click(button('Play Live Photo'));
      for (let tries = 0; tries < 20 && !video().hasAttribute('src'); tries++) await advance(0);
      expect(video().hasAttribute('src')).toBe(true);
    }

    async function watched() {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await advance(2000);
    }

    it('says Live, rings while its video loads, and pulses while it plays', async () => {
      const live = () => button('Play Live Photo').getAttribute('data-live');
      const { container } = await openStory('/day/2026-11-11?year=2025');
      await shown();

      expect(button('Play Live Photo')).toHaveTextContent('Live');
      expect(live()).toBe('still');
      await replay();
      expect(live()).toBe('loading');
      expect(await axeResults(container)).toHaveNoViolations();
      await fire('playing');
      expect(live()).toBe('playing');
      await fire('ended');
      expect(live()).toBe('still');
      await replay();
      expect(live()).toBe('loading');
      await fire('playing');
      expect(live()).toBe('playing');
      await fire('error');
      expect(live()).toBe('still');
    });

    it('stays still when its video reports playing after it ended', async () => {
      const live = () => button('Play Live Photo').getAttribute('data-live');
      await watched();
      await replay();
      await fire('playing');
      await fire('ended');

      await fire('playing');

      expect(live()).toBe('still');
      expect(video()).not.toBeVisible();
      await advance(2999);
      expect(showing()).toBe(preview(ids.a2025));
      await advance(1);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('stays still while the story is paused', async () => {
      const live = () => button('Play Live Photo').getAttribute('data-live');
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await replay();

      await user().click(button('Pause'));
      expect(live()).toBe('still');
      await user().click(button('Play'));
      expect(live()).toBe('loading');
      await fire('playing');
      await user().click(button('Pause'));
      expect(live()).toBe('still');
    });

    it('plays the video and holds the story still while it plays', async () => {
      const { container } = await openStory('/day/2026-11-11?year=2025');
      await shown();
      await advance(2000);
      const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

      await replay();
      expect(video().getAttribute('src')).toBe(keptSource);
      expect(video().muted).toBe(true);
      expect(play).toHaveBeenCalledOnce();
      await fire('playing');
      expect(video()).toBeVisible();
      expect(segment(0).style.animationPlayState).toBe('paused');
      await advance(30_000);
      expect(showing()).toBe(preview(ids.a2025));
      expect(await axeResults(container)).toHaveNoViolations();
      await fire('ended');

      expect(video()).not.toBeVisible();
      expect(segment(0).style.animationPlayState).toBe('running');
      await advance(2999);
      expect(showing()).toBe(preview(ids.a2025));
      await advance(1);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('keeps playing when a press ends anywhere else', async () => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      act(() => {
        button('Play Live Photo').focus();
      });
      await user().keyboard('{Enter}');
      expect(showing()).toBe(preview(ids.a2025));
      await fire('playing');

      fireEvent.pointerUp(window);
      await advance(0);

      expect(video()).toBeVisible();
      expect(video().hasAttribute('src')).toBe(true);
    });

    it('lets the story play on when the video breaks', async () => {
      await watched();
      await replay();
      await fire('playing');
      await advance(30_000);

      await fire('error');

      expect(video()).not.toBeVisible();
      expect(segment(0).style.animationPlayState).toBe('running');
      await advance(2999);
      expect(showing()).toBe(preview(ids.a2025));
      await advance(1);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('lets the story run until the video plays', async () => {
      await watched();
      await replay();

      expect(video()).not.toBeVisible();
      expect(segment(0).style.animationPlayState).toBe('running');
      await advance(3000);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('plays the video again from its start during a play, and holds the story', async () => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await advance(1000);
      await replay();
      await fire('playing');
      const src = vi.spyOn(HTMLMediaElement.prototype, 'src', 'set');

      await replay();
      expect(src.mock.calls).toEqual([[keptSource]]);
      expect(video()).not.toBeVisible();
      await fire('playing');
      await advance(30_000);
      expect(showing()).toBe(preview(ids.a2025));
      await fire('ended');

      await advance(3999);
      expect(showing()).toBe(preview(ids.a2025));
      await advance(1);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('plays the story on when it was paused', async () => {
      await watched();
      await user().click(button('Pause'));
      await advance(30_000);
      const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

      await replay();

      expect(play).toHaveBeenCalledOnce();
      expect(button('Pause')).toBeVisible();
      await fire('playing');
      await fire('ended');
      await advance(3000);
      expect(showing()).toBe(preview(ids.b2025));
    });

    it('plays the video when the story rests on it at its end', async () => {
      years = [yearGroup(2025, live(ids.a2025, 2025))];
      await watched();
      await advance(10_000);
      const play = vi.spyOn(HTMLMediaElement.prototype, 'play');

      await replay();
      expect(play).toHaveBeenCalledOnce();
      await fire('playing');
      expect(video()).toBeVisible();
      await fire('ended');

      await advance(60_000);
      expect(showing()).toBe(preview(ids.a2025));
      expect(segment(0)).toHaveStyle({ animationPlayState: 'paused' });
    });

    it('lets the next memory run on its own clock when the story moves on during a replay', async () => {
      previewsNotPrefetched();
      years.push(yearGroup(2023, memory(ids.a2023, 2023)));
      await watched();
      await replay();
      await fire('playing');

      await user().click(button('Next'));
      await shown();

      expect(video().hasAttribute('src')).toBe(false);
      await advance(4999);
      expect(showing()).toBe(preview(ids.b2025));
      await advance(1);
      expect(await shown()).not.toBe(preview(ids.b2025));
    });
  });

  describe('its video, kept loaded', () => {
    const movOf = (id: string) => id.replace('0000000', '0000mov');
    const motionOf = (id: string) => proxyUrl(movOf(id), 'video');
    const liveAt = (id: string, year: number) =>
      memory(id, year, {
        mediaType: 'LivePhoto',
        livePhotoVideoUrl: proxyPath(movOf(id), 'video'),
      });
    let received: string[];
    let answer: (url: string) => Promise<Response | undefined> | undefined;
    const fetched = () => received.filter((url) => url.includes('mov'));

    beforeEach(() => {
      received = [];
      answer = () => undefined;
      server.use(proxyAnswers(received, (url) => answer(url)));
    });

    async function fetchedVideos(...urls: string[]) {
      await waitFor(() => {
        expect(fetched()).toEqual(urls);
      });
    }

    it('plays from memory when the story comes back to it', async () => {
      years = [yearGroup(2025, liveAt(ids.a2025, 2025), memory(ids.b2025, 2025))];
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await fetchedVideos(motionOf(ids.a2025));

      fireEvent.click(button('Next'));
      await shown();
      fireEvent.click(button('Previous'));
      await shown();
      fireEvent.click(button('Play Live Photo'));

      expect(video().getAttribute('src')).toBe(fromMemoryOf(motionOf(ids.a2025)));
      expect(fetched()).toEqual([motionOf(ids.a2025)]);
    });

    it('downloads no video before the previews of the memory showing and the next two', async () => {
      let arrive: () => void = () => undefined;
      answer = (url) =>
        url === preview(ids.b2025)
          ? new Promise((resolve) => {
              arrive = () => {
                resolve(undefined);
              };
            })
          : undefined;
      years = [yearGroup(2025, liveAt(ids.a2025, 2025), liveAt(ids.b2025, 2025))];
      await openStory('/day/2026-11-11?year=2025');
      await advance(1000);
      expect(fetched()).toEqual([]);

      arrive();

      await fetchedVideos(motionOf(ids.a2025), motionOf(ids.b2025));
      expect(received.indexOf(motionOf(ids.a2025))).toBeGreaterThan(
        received.indexOf(preview(ids.b2025)),
      );
    });

    it('downloads the videos of the Live Photo showing and the next one, and plays them from memory', async () => {
      years = [
        yearGroup(2025, memory(ids.a2025, 2025), liveAt(ids.b2025, 2025)),
        yearGroup(2023, liveAt(ids.a2023, 2023), liveAt(ids.a2021, 2023)),
      ];
      await openStory('/day/2026-11-11?year=2025');
      await fetchedVideos(motionOf(ids.b2025));

      fireEvent.click(button('Next'));
      await shown();
      await fetchedVideos(motionOf(ids.b2025), motionOf(ids.a2023));
      fireEvent.click(button('Play Live Photo'));

      expect(video().getAttribute('src')).toBe(fromMemoryOf(motionOf(ids.b2025)));
    });

    it('downloads no video of a burst', async () => {
      years = [
        yearGroup(
          2025,
          memory(ids.a2025, 2025, {
            mediaType: 'LivePhoto',
            livePhotoVideoUrl: proxyPath(movOf(ids.a2025), 'video'),
            burst: { burstId: ids.c2025, isCover: true, count: 2 },
          }),
          liveAt(ids.b2025, 2025),
        ),
      ];
      await openStory('/day/2026-11-11?year=2025');

      await fetchedVideos(motionOf(ids.b2025));
      expect(fetched()).toEqual([motionOf(ids.b2025)]);
    });

    it('downloads the next Live Photo video past a photo', async () => {
      years = [
        yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025)),
        yearGroup(2023, liveAt(ids.a2023, 2023)),
      ];
      await openStory('/day/2026-11-11?year=2025');

      await fetchedVideos(motionOf(ids.a2023));
    });

    it('keeps every video it downloaded, even once the story closes', async () => {
      const order = [ids.a2025, ids.b2025, ids.c2025, ids.a2023];
      years = [yearGroup(2025, ...order.map((id) => liveAt(id, 2025)))];
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      for (let step = 0; step < 3; step++) {
        fireEvent.click(button('Next'));
        await shown();
      }
      for (let step = 0; step < 3; step++) {
        fireEvent.click(button('Previous'));
        await shown();
      }
      await fetchedVideos(...order.map(motionOf));

      fireEvent.click(button('Close'));
      await advance(0);

      expect(freedSources()).toEqual([]);
    });

    it('aborts a video download the story moved past', async () => {
      const aborted: string[] = [];
      server.use(
        http.get('/api/v1/proxies/:id/video', ({ request }) => {
          received.push(new URL(request.url).pathname + new URL(request.url).search);
          request.signal.addEventListener('abort', () => {
            aborted.push(request.url);
          });
          return new Promise<Response>(() => undefined);
        }),
      );
      years = [
        yearGroup(2025, liveAt(ids.a2025, 2025), memory(ids.b2025, 2025), memory(ids.c2025, 2025)),
      ];
      await openStory('/day/2026-11-11?year=2025');
      await fetchedVideos(motionOf(ids.a2025));

      fireEvent.click(button('Next'));
      await advance(0);

      expect(aborted).toHaveLength(1);
    });

    it('plays a video asked for before it arrived from memory, without a second download', async () => {
      let arrive: () => void = () => undefined;
      answer = (url) =>
        url.includes('mov')
          ? new Promise((resolve) => {
              arrive = () => {
                resolve(undefined);
              };
            })
          : undefined;
      years = [yearGroup(2025, liveAt(ids.a2025, 2025), memory(ids.b2025, 2025))];
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await fetchedVideos(motionOf(ids.a2025));

      fireEvent.click(button('Play Live Photo'));
      await advance(0);
      expect(video().hasAttribute('src')).toBe(false);
      arrive();

      await waitFor(() => {
        expect(video().getAttribute('src')).toBe(fromMemoryOf(motionOf(ids.a2025)));
      });
      expect(fetched()).toEqual([motionOf(ids.a2025)]);
    });

    it('downloads a video asked for ahead of the previews still to come', async () => {
      let arrive: () => void = () => undefined;
      answer = (url) =>
        url === preview(ids.b2025)
          ? new Promise((resolve) => {
              arrive = () => {
                resolve(undefined);
              };
            })
          : undefined;
      years = [yearGroup(2025, liveAt(ids.a2025, 2025), memory(ids.b2025, 2025))];
      await openStory('/day/2026-11-11?year=2025');
      await shown();

      fireEvent.click(button('Play Live Photo'));

      await fetchedVideos(motionOf(ids.a2025));
      arrive();
      await waitFor(() => {
        expect(received.filter((url) => url === preview(ids.b2025))).toHaveLength(2);
      });
    });

    it('plays from the network a video the server refuses', async () => {
      answer = (url) =>
        Promise.resolve(url.includes('mov') ? problemResponse(404, 'not_found') : undefined);
      years = [yearGroup(2025, liveAt(ids.a2025, 2025), memory(ids.b2025, 2025))];
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await fetchedVideos(motionOf(ids.a2025));
      await advance(0);

      fireEvent.click(button('Play Live Photo'));

      expect(video().getAttribute('src')).toBe(motionOf(ids.a2025));
      expect(fetched()).toHaveLength(1);
    });
  });
});

describe('the next memories', () => {
  const clip = (id: string) => proxyUrl(id, 'video');
  const film = (id: string, year: number) =>
    memory(id, year, {
      mediaType: 'Video',
      durationSeconds: 12,
      proxies: {
        thumbnail: proxyPath(id, 'thumbnail'),
        preview: proxyPath(id, 'preview'),
        compactPreview: null,
        video: proxyPath(id, 'video'),
      },
    });
  const preloading = () =>
    [...story().querySelectorAll('video[preload="auto"]')].map((video) =>
      video.getAttribute('src'),
    );
  let received: string[];
  let decoded: string[];
  let decode: MockInstance<HTMLImageElement['decode']>;
  let answer: (url: string) => Response | Promise<undefined> | undefined;

  async function arrived(...urls: string[]) {
    await waitFor(() => {
      expect(decoded).toEqual(urls);
    });
    expect(received).toEqual(urls);
  }

  function record(image: HTMLImageElement) {
    const src = (image.getAttribute('src') ?? '').replace(/^memory:/, '');
    if (!image.isConnected && received.includes(src)) decoded.push(src);
  }

  function holdOne(url: string) {
    let release: () => void = () => undefined;
    const until = new Promise<void>((resolve) => {
      release = resolve;
    });
    const before = answer;
    answer = (asked) => (asked === url ? until.then(() => undefined) : before(asked));
    return release;
  }

  beforeEach(() => {
    received = [];
    decoded = [];
    answer = () => undefined;
    decode = vi.spyOn(HTMLImageElement.prototype, 'decode');
    decode.mockImplementation(function (this: HTMLImageElement) {
      record(this);
      return Promise.resolve();
    });
    server.use(proxyAnswers(received, (url) => answer(url)));
  });

  it("downloads the memory showing, then the next two memories' previews, across a year group's end", async () => {
    await openStory('/day/2026-11-11?year=2025');

    await arrived(preview(ids.a2025), preview(ids.b2025), preview(ids.a2023));
    fireEvent.click(button('Next'));
    await shown();

    await arrived(preview(ids.a2025), preview(ids.b2025), preview(ids.a2023), preview(ids.a2021));
  });

  it('downloads ahead of a later year group only the memories after it', async () => {
    await openStory('/day/2026-11-11?year=2023');

    await arrived(preview(ids.a2023), preview(ids.a2021), preview(ids.b2021));
  });

  it('downloads each preview once, however often the story passes it, and after it reopens', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2025');
    await shown();
    fireEvent.click(button('Next'));
    await shown();
    fireEvent.click(button('Previous'));
    await shown();
    fireEvent.click(button('Next'));
    await shown();
    await arrived(preview(ids.a2025), preview(ids.b2025), preview(ids.a2023), preview(ids.a2021));

    fireEvent.click(button('Close'));
    await advance(0);
    await act(() =>
      router.navigate({
        to: '/day/$date',
        params: { date: '2026-11-11' },
        search: { year: 2025 },
      }),
    );
    await shown();

    const previews = [
      preview(ids.a2025),
      preview(ids.b2025),
      preview(ids.a2023),
      preview(ids.a2021),
    ];
    const isPreview = (url: string) => url.includes('/preview?');
    await waitFor(() => {
      expect(decoded.filter(isPreview)).toEqual(previews);
    });
    expect(received.filter(isPreview)).toEqual(previews);
    expect(received.filter((url) => !isPreview(url))).toEqual([
      thumbnail(ids.a2023),
      thumbnail(ids.a2021),
    ]);
  });

  it('downloads ahead of a failed memory too', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await advance(10_000);

    await arrived(preview(ids.a2025), preview(ids.b2025), preview(ids.a2023));
  });

  it('downloads ahead of a memory without a preview at once, and skips one ahead', async () => {
    const bare = { thumbnail: null, preview: null, compactPreview: null, video: null };
    years = [
      yearGroup(2025, memory(ids.a2025, 2025, { proxies: bare })),
      yearGroup(2023, memory(ids.a2023, 2023, { proxies: bare }), memory(ids.b2025, 2023)),
    ];
    renderApp('/day/2026-11-11?year=2025');

    await arrived(preview(ids.b2025));
  });

  it('skips a hidden memory', async () => {
    server.use(interactionAnswers([]));
    await openStory('/day/2026-11-11?year=2025');
    fireEvent.click(button('Next'));
    fireEvent.click(button('Hide'));
    await shown();

    await arrived(
      preview(ids.a2025),
      preview(ids.b2025),
      preview(ids.a2023),
      preview(ids.a2021),
      preview(ids.b2021),
    );
  });

  it('decodes a preview only once all of it has arrived, and downloads the next only then', async () => {
    let finish: () => void = () => undefined;
    answer = (url) => {
      if (url !== preview(ids.b2025)) return undefined;
      const body = new ReadableStream({
        start(controller) {
          controller.enqueue(new Uint8Array([1]));
          finish = () => {
            controller.close();
          };
        },
      });
      return new Response(body);
    };
    await openStory('/day/2026-11-11?year=2025');
    await waitFor(() => {
      expect(received).toEqual([preview(ids.a2025), preview(ids.b2025)]);
    });
    await advance(0);
    expect(decoded).toEqual([preview(ids.a2025)]);

    finish();

    await arrived(preview(ids.a2025), preview(ids.b2025), preview(ids.a2023));
  });

  it('preloads only the next video, within the next two memories, once their previews arrived, and keeps it while moving', async () => {
    years = [
      yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025), memory(ids.c2025, 2025)),
      yearGroup(2023, film(ids.a2023, 2023), film(ids.a2021, 2023)),
    ];
    const held = holdOne(preview(ids.a2023));
    await openStory('/day/2026-11-11?year=2025');
    expect(preloading()).toEqual([]);
    fireEvent.click(button('Next'));
    await advance(0);
    expect(preloading()).toEqual([]);

    held();
    await waitFor(() => {
      expect(preloading()).toEqual([clip(ids.a2023)]);
    });
    const preloaded = story().querySelector('video[preload="auto"]');
    fireEvent.click(button('Next'));
    await advance(0);

    expect(preloading()).toEqual([clip(ids.a2023)]);
    expect(story().querySelector('video[preload="auto"]')).toBe(preloaded);
  });

  it('downloads only the cover of a burst ahead, not its frames', async () => {
    const burstId = '0199a1b2-c3d4-7e5f-8a9b-0000000b0000';
    years = [
      yearGroup(
        2025,
        memory(ids.a2025, 2025),
        memory(ids.b2025, 2025, { burst: { burstId, isCover: true, count: 8 } }),
      ),
    ];
    const frames = vi.fn();
    server.use(http.get('/api/v1/bursts/:burstId/assets', frames));
    await openStory('/day/2026-11-11?year=2025');

    await arrived(preview(ids.a2025), preview(ids.b2025));
    expect(frames).not.toHaveBeenCalled();
  });
});

describe('a blurhash', () => {
  const plum = [200, 40, 90, 255];
  const solid = (colour: number[]) =>
    encode(new Uint8ClampedArray(Array.from({ length: 16 }, () => colour).flat()), 4, 4, 1, 1);
  let painted: { width: number; height: number; data: Uint8ClampedArray }[];
  const canvas = () => story().querySelector('canvas');

  beforeEach(() => {
    painted = [];
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
      createImageData: (width: number, height: number) => ({
        width,
        height,
        data: new Uint8ClampedArray(width * height * 4),
      }),
      putImageData: (image: (typeof painted)[number], x: number, y: number) => {
        expect([x, y]).toEqual([0, 0]);
        painted.push(image);
      },
    } as unknown as CanvasRenderingContext2D);
  });

  const withBlurhash = (overrides: Parameters<typeof memory>[2] = {}) => {
    years = [
      yearGroup(2025, memory(ids.a2025, 2025, { blurhash: solid(plum), ...overrides })),
      yearGroup(2023, memory(ids.a2023, 2023, { blurhash: solid(plum) })),
    ];
  };

  const lastPainted = () => painted.at(-1) as (typeof painted)[number];
  const pixels = (image: (typeof painted)[number]) =>
    Array.from({ length: image.width * image.height }, (_, pixel) => [
      ...image.data.slice(pixel * 4, pixel * 4 + 4),
    ]);

  it('shows behind a picture until it is decoded', async () => {
    withBlurhash();
    const { container } = await openStory('/day/2026-11-11?year=2025');

    expect(canvas()?.compareDocumentPosition(picture())).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    expect([canvas()?.width, canvas()?.height]).toEqual([32, 24]);
    const image = lastPainted();
    expect([image.width, image.height]).toEqual([32, 24]);
    expect(new Set(pixels(image).map(String))).toEqual(new Set([String(plum)]));
    expect(await axeResults(container)).toHaveNoViolations();

    await shown();
    expect(canvas()).toBeNull();
  });

  it.each([
    [3024, 4032, [24, 32]],
    [4000, 10, [32, 1]],
    [null, null, [32, 32]],
  ])('keeps the shape of a %s × %s picture', async (width, height, size) => {
    withBlurhash({ width, height });
    await openStory('/day/2026-11-11?year=2025');

    expect([canvas()?.width, canvas()?.height]).toEqual(size);
  });

  describe('of a memory whose preview was fetched ahead', () => {
    const live = {
      livePhotoVideoUrl: proxyPath('0199a1b2-c3d4-7e5f-8a9b-00000000a0e1', 'video'),
    };
    const burst = { burst: { burstId: ids.c2025, isCover: true, count: 2 } };
    let received: string[];

    function threeYears(overrides: Parameters<typeof memory>[2] = {}) {
      years = [
        yearGroup(2025, memory(ids.a2025, 2025, { blurhash: solid(plum) })),
        yearGroup(2023, memory(ids.a2023, 2023, { blurhash: solid(plum), ...overrides })),
        yearGroup(2021, memory(ids.a2021, 2021, { blurhash: solid(plum) })),
      ];
    }

    async function openFetchingAhead() {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      fireEvent.click(button('Pause'));
      await waitFor(() => {
        expect(received.filter((url) => url.includes('/preview?'))).toEqual([
          preview(ids.a2025),
          preview(ids.a2023),
          preview(ids.a2021),
        ]);
      });
      await advance(0);
      fireEvent.click(button('Play'));
      painted = [];
    }

    beforeEach(() => {
      received = [];
      server.use(proxyAnswers(received));
    });

    it('is never shown when the timer moves on to it, which then shows for 5 s', async () => {
      threeYears();
      await openFetchingAhead();

      await advance(5000);
      expect(showing()).toBe(preview(ids.a2023));
      expect(canvas()).toBeNull();
      expect(painted).toEqual([]);

      await advance(4999);
      expect(showing()).toBe(preview(ids.a2023));
      await advance(1);
      expect(showing()).toBe(preview(ids.a2021));
      expect(painted).toEqual([]);
    });

    it.each([
      ['a photo', {}],
      ['a Live Photo', live],
      ['a burst', burst],
    ])('is never shown when a tap moves on to %s', async (_, overrides) => {
      threeYears(overrides);
      await openFetchingAhead();

      fireEvent.click(button('Next'));
      await advance(0);

      expect(showing()).toBe(preview(ids.a2023));
      expect(canvas()).toBeNull();
      expect(painted).toEqual([]);
    });

    it.each([
      ['refused', () => new HttpResponse(null, { status: 503 })],
      ['not sent', () => HttpResponse.error()],
    ])('is shown for a memory whose preview was %s ahead', async (_, answer) => {
      let refusing = true;
      server.use(
        proxyAnswers([], (url) => {
          if (url !== preview(ids.a2023) || !refusing) return undefined;
          refusing = false;
          return answer();
        }),
      );
      threeYears();
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      await advance(0);

      fireEvent.click(button('Next'));
      await advance(0);

      expect(canvas()).not.toBeNull();
      expect(await shown()).toBe(preview(ids.a2023));
    });

    it('still waits for a video to play', async () => {
      threeYears({
        mediaType: 'Video',
        durationSeconds: 12,
        proxies: {
          thumbnail: proxyPath(ids.a2023, 'thumbnail'),
          preview: proxyPath(ids.a2023, 'preview'),
          compactPreview: null,
          video: proxyPath(ids.a2023, 'video'),
        },
      });
      await openFetchingAhead();

      fireEvent.click(button('Next'));
      await advance(0);

      expect(canvas()).not.toBeNull();
      expect(segment(0).style.animationPlayState).toBe('paused');
    });

    it('fails a picture that breaks, without reloading it', async () => {
      threeYears();
      await openFetchingAhead();
      fireEvent.click(button('Next'));
      await advance(0);

      fireEvent.error(picture());
      await advance(0);

      expect(within(story()).getByText(failure)).toBeVisible();
      expect(picture()).toBeNull();
    });
  });

  it("shows with a failed memory's message", async () => {
    withBlurhash();
    await openStory('/day/2026-11-11?year=2025');

    await advance(10_000);

    expect(within(story()).getByText(failure)).toBeVisible();
    expect(canvas()).not.toBeNull();
    expect(new Set(pixels(lastPainted()).map(String))).toEqual(new Set([String(plum)]));
  });

  it('does not show for a memory without a preview', async () => {
    withBlurhash({
      proxies: { thumbnail: null, preview: null, compactPreview: null, video: null },
    });
    renderApp('/day/2026-11-11?year=2025');

    const memory = await screen.findByRole('img', { name: 'Photo, 2025, 1 of 1' });
    expect(memory).toBeEmptyDOMElement();
  });

  it('leaves the player surface for a memory without one', async () => {
    await openStory('/day/2026-11-11?year=2025');
    expect(canvas()).toBeNull();

    await advance(10_000);

    expect(within(story()).getByText(failure)).toBeVisible();
    expect(canvas()).toBeNull();
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
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });

    close();
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
    expect(router.history.canGoBack()).toBe(false);
  });

  it('stops playing once closed, so it fetches nothing more ahead', async () => {
    const received: string[] = [];
    server.use(proxyAnswers(received));
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    fireEvent.click(button('Close'));
    await advance(0);
    expect(screen.queryByRole('dialog', { name: storyName })).toBeNull();

    await advance(60_000);

    expect(received).not.toContain(preview(ids.a2021));
  });

  it("with the browser's back button closes it", async () => {
    const { router } = await openLanding();
    fireEvent.click(todayCard());
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
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });
    years = [yearGroup(2023, memory(ids.a2023, 2023))];
    expect(requested).toEqual(['2026-11-11']);

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));

    await advance(0);
    expect(requested).toEqual(['2026-11-11', '2026-11-11']);
    await waitFor(() => {
      expect(todayCard().querySelector('img')).toHaveAttribute('src', thumbnail(ids.a2023));
    });
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
    await waitFor(() => {
      expect(requested.toSorted()).toEqual(['2024-11-11', '2026-11-11']);
    });

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(1000);

    expect(requested.filter((date) => date === '2026-11-11')).toHaveLength(1);
  });

  it('leaves the landing screen scrolled where it was', async () => {
    await openLanding();
    const tray = screen.getByRole('list', { name: 'Recent days' });
    tray.scrollLeft = 120;
    Object.defineProperty(window, 'scrollY', { value: 300, configurable: true });
    fireEvent.scroll(document);
    const scrollTo = vi.spyOn(window, 'scrollTo');

    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });
    expect(scrollTo).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await advance(0);

    expect(screen.getByRole('list', { name: 'Recent days' })).toBe(tray);
    expect(tray.scrollLeft).toBe(120);
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

    expect(await shown()).toBe(preview(ids.a2023));
    await user().click(button('Previous'));
    expect(await shown()).toBe(preview(ids.b2025));
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
    previewsNotPrefetched();
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(3000);

    await user().click(button('Next'));
    await advance(2000);
    expect(await shown()).toBe(preview(ids.b2025));
    await advance(4999);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('starts no clock from a picture it moved away from before it decoded', async () => {
    previewsNotPrefetched();
    await openStory('/day/2026-11-11?year=2025');

    fireEvent.load(picture());
    fireEvent.click(button('Next'));
    await advance(9999);

    expect(within(story()).queryByText(failure)).toBeNull();
    expect(segment(1)).toHaveStyle({ animationPlayState: 'paused' });
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
    ).toEqual(['Previous', 'Next', 'Pause', 'Close', 'Star', 'Like', 'Hide']);
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
    act(() => {
      button('Next').focus();
    });

    await user().keyboard('{Enter}');
    expect(showing()).toBe(preview(ids.b2025));
    act(() => {
      button('Previous').focus();
    });
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
    act(() => {
      button('Next').focus();
    });

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
    act(() => {
      button('Next').focus();
    });
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

describe('pulling a story down', () => {
  const card = () => middle().parentElement?.parentElement as HTMLElement;
  const backdrop = () => card().previousElementSibling as HTMLElement;
  const fill = () => card().firstElementChild as HTMLElement;
  const slowly = 500;

  it('shrinks the story into a rounded card that follows the finger, and fades the backdrop', async () => {
    await openStory('/day/2026-11-11?year=2023');
    expect(backdrop()).toHaveClass('bg-player');
    expect(card().style.transform).toBe('none');

    const release = await drag(0, 384);

    expect(card().style.transform).toBe('translateY(384px) scale(0.8)');
    expect(card().style.borderRadius).toBe('24px');
    expect(fill()).toHaveClass('bg-player');
    expect(fill().style.opacity).toBe('1');
    expect(backdrop().style.opacity).toBe('0.5');
    await release();
  });

  it('shrinks no further than a card of 60%, with no backdrop left', async () => {
    await openStory('/day/2026-11-11?year=2023');

    const release = await drag(0, 1000);

    expect(card().style.transform).toBe('translateY(1000px) scale(0.6)');
    expect(backdrop().style.opacity).toBe('0');
    await release();
  });

  it('springs back when released short of a third of the screen', async () => {
    await openStory('/day/2026-11-11?year=2023');

    await swipe(0, 255, middle(), slowly);

    expect(story()).toBeVisible();
    expect(card().style.transform).toBe('none');
    expect(card().style.borderRadius).toBe('0px');
    expect(backdrop().style.opacity).toBe('1');
  });

  it('closes when released past a third of the screen', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2023');

    await swipe(0, 256, middle(), slowly);
    await advance(0);

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(router.state.location.href).toBe('/');
  });

  it('springs back from a slow short pull', async () => {
    await openStory('/day/2026-11-11?year=2023');

    await swipe(0, 60, middle(), slowly);

    expect(story()).toBeVisible();
  });

  it('only fades the backdrop under reduced motion', async () => {
    reduceMotion();
    await openStory('/day/2026-11-11?year=2023');

    const release = await drag(0, 384);

    expect(card().style.transform).toBe('none');
    expect(card().style.borderRadius).toBe('0px');
    expect(fill().style.opacity).toBe('0.5');
    expect(backdrop().style.opacity).toBe('0.5');
    await release();
  });

  it('pulls along a drag exactly as far down as sideways', async () => {
    await openStory('/day/2026-11-11?year=2023');

    const release = await drag(-384, 384);

    expect(backdrop().style.opacity).toBe('0.5');
    await release();
  });

  it('neither shrinks the story nor fades the backdrop when swiped sideways, even dipping down', async () => {
    await openStory('/day/2026-11-11?year=2025');

    const release = await drag(-200, 150);

    expect(card().style.transform).toBe('translateY(150px)');
    expect(backdrop().style.opacity).toBe('1');
    await release();
  });
});

describe('pausing a story', () => {
  it('focuses the pause button when the story opens', async () => {
    await openLanding();
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });
    await advance(0);

    expect(button('Pause')).toHaveFocus();
  });

  it('pauses with the pause button and plays on from where it stopped', async () => {
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(3000);

    await user().click(button('Pause'));
    await advance(30_000);
    expect(showing()).toBe(preview(ids.a2025));
    expect(await axeResults(container)).toHaveNoViolations();

    await user().click(button('Play'));
    await advance(1999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('focuses the pause button of a reloaded story once its day arrives', async () => {
    await openStory('/day/2026-11-11?year=2025');

    expect(button('Pause')).toHaveFocus();
  });

  it('keeps a paused story paused when moving, with a full 5 s for each memory', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(3000);
    await user().click(button('Pause'));

    await user().click(button('Next'));
    await shown();
    await advance(30_000);
    expect(showing()).toBe(preview(ids.b2025));
    expect(button('Play')).toBeVisible();

    await user().click(button('Play'));
    await advance(4999);
    expect(showing()).toBe(preview(ids.b2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.a2023));
  });

  it('pauses twice in one memory and keeps what was left each time', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();

    for (const played of [1000, 2000]) {
      await advance(played);
      await user().click(button('Pause'));
      await advance(10_000);
      await user().click(button('Play'));
    }
    await advance(1999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('starts the clock of a paused memory only once it is played and its picture decoded', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await user().click(button('Pause'));
    await shown();
    await advance(10_000);
    expect(showing()).toBe(preview(ids.a2025));

    await user().click(button('Play'));
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('holds the progress segment still while paused', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    const segment = () => story().querySelector('ol > li > span') as HTMLElement;
    const first = segment();

    await user().click(button('Pause'));
    expect(segment().style.animationPlayState).toBe('paused');
    await user().click(button('Play'));
    expect(segment().style.animationPlayState).toBe('running');
    expect(segment()).toBe(first);
  });

  it('toggles with Space from the focused pause button', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().keyboard(' ');
    await advance(10_000);
    expect(showing()).toBe(preview(ids.a2025));
    expect(button('Play')).toHaveFocus();

    await user().keyboard(' ');
    await advance(5000);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('toggles with Space while no button has focus', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    act(() => {
      story().focus();
    });

    await user().keyboard(' ');
    await advance(10_000);
    expect(showing()).toBe(preview(ids.a2025));
    expect(button('Play')).toBeVisible();

    await user().keyboard(' ');
    await advance(5000);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it.each(['{Shift>} {/Shift}', '{Control>} {/Control}', '{Alt>} {/Alt}', '{Meta>} {/Meta}', 'p'])(
    'does not toggle with %s',
    async (keys) => {
      await openStory('/day/2026-11-11?year=2025');
      await shown();
      act(() => {
        story().focus();
      });

      await user().keyboard(keys);
      await advance(5000);

      expect(showing()).toBe(preview(ids.b2025));
    },
  );

  it('goes back with Space on a focused Previous button and keeps playing', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await user().click(button('Next'));
    act(() => {
      button('Previous').focus();
    });

    await user().keyboard(' ');
    await shown();
    await advance(5000);

    expect(showing()).toBe(preview(ids.b2025));
  });

  it('toggles once while Space is held down', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    act(() => {
      story().focus();
    });

    await user().keyboard('{ >2}');
    await advance(10_000);

    expect(showing()).toBe(preview(ids.a2025));
  });

  it('pauses while held and hides the controls but the years ago, and plays on when released', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(1000);
    const pointer = user();

    await pointer.pointer({ keys: '[MouseLeft>]', target: button('Next') });
    await advance(30_000);
    expect(showing()).toBe(preview(ids.a2025));
    expect(within(story()).queryByRole('button', { name: 'Pause' })).toBeNull();
    expect(within(story()).queryByRole('button', { name: 'Close' })).toBeNull();
    expect(within(story()).getByText('1 year ago · 2025')).toBeVisible();
    expect(story().querySelector('ol')).toBeVisible();
    expect(segment(0)).toHaveStyle({ animationPlayState: 'paused' });

    await pointer.pointer({ keys: '[/MouseLeft]', target: button('Next') });
    expect(button('Pause')).toBeVisible();
    await advance(3499);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it.each([
    ['the middle', middle],
    ['Previous', () => button('Previous')],
  ])('neither moves nor stays paused after a hold on %s', async (_, zone) => {
    previewsNotPrefetched();
    await openStory('/day/2026-11-11?year=2025');
    await user().click(button('Next'));
    await shown();
    const pointer = user();

    await pointer.pointer({ keys: '[MouseLeft>]', target: zone() });
    await advance(10_000);
    await pointer.pointer({ keys: '[/MouseLeft]', target: zone() });

    expect(showing()).toBe(preview(ids.b2025));
    await advance(4499);
    expect(showing()).toBe(preview(ids.b2025));
    await advance(1);
    expect(await shown()).toBe(preview(ids.a2023));
  });

  it('moves on a press shorter than a hold', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    const pointer = user();

    await pointer.pointer({ keys: '[MouseLeft>]', target: button('Next') });
    await advance(499);
    await pointer.pointer({ keys: '[/MouseLeft]', target: button('Next') });

    expect(showing()).toBe(preview(ids.b2025));
  });

  it('plays on when the browser cancels a hold', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().pointer({ keys: '[MouseLeft>]', target: button('Next') });
    await advance(10_000);

    fireEvent.pointerCancel(window);
    await advance(4499);
    expect(showing()).toBe(preview(ids.a2025));
    expect(button('Pause')).toBeVisible();
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('stays paused by the pause button when a hold is released', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Pause'));
    const pointer = user();

    await pointer.pointer({ keys: '[MouseLeft>]', target: middle() });
    await advance(1000);
    await pointer.pointer({ keys: '[/MouseLeft]', target: middle() });
    await advance(30_000);

    expect(showing()).toBe(preview(ids.a2025));
    expect(button('Play')).toBeVisible();
  });

  it('pauses again with the button after a hold', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    const pointer = user();
    await pointer.pointer({ keys: '[MouseLeft>]', target: middle() });
    await advance(1000);
    await pointer.pointer({ keys: '[/MouseLeft]', target: middle() });

    await user().click(button('Pause'));
    fireEvent.pointerUp(window);
    await advance(30_000);

    expect(showing()).toBe(preview(ids.a2025));
  });

  it('stays closed when a hold is released after closing', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2021');
    await user().click(button('Next'));
    await shown();
    await user().pointer({ keys: '[MouseLeft>]', target: middle() });
    await advance(1000);
    fireEvent.keyDown(story(), { key: 'Escape' });
    await advance(0);
    const closed = router.history.location.state.__TSR_key;

    fireEvent.pointerUp(window);
    await advance(5000);

    expect(router.history.location.state.__TSR_key).toBe(closed);
  });
});

describe('a story for a screen reader', () => {
  const named = (name: string) => within(story()).getByRole('img', { name });

  it('names each memory by its kind, year and place in its year group', async () => {
    const withVideo = (id: string, mediaType: Exclude<Memory['mediaType'], 'Image'>) =>
      memory(id, 2021, {
        mediaType,
        proxies: {
          thumbnail: null,
          preview: proxyPath(id, 'preview'),
          compactPreview: null,
          video: proxyPath(id, 'video'),
        },
      });
    years = [
      yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025)),
      yearGroup(2021, withVideo(ids.a2021, 'Video'), withVideo(ids.b2021, 'LivePhoto')),
    ];
    const { container } = await openStory('/day/2026-11-11?year=2025');

    expect(named('Photo, 2025, 1 of 2')).toContainElement(picture());
    await user().click(button('Next'));
    expect(named('Photo, 2025, 2 of 2')).toBeVisible();
    await user().click(button('Next'));
    expect(named('Video, 2021, 1 of 2')).toBeVisible();
    await user().click(button('Next'));
    expect(named('Live Photo, 2021, 2 of 2')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('names a memory without a preview too', async () => {
    years = [
      yearGroup(
        2023,
        memory(ids.a2023, 2023, {
          proxies: { thumbnail: null, preview: null, compactPreview: null, video: null },
        }),
      ),
    ];
    renderApp('/day/2026-11-11?year=2023');

    expect(
      await within(await screen.findByRole('dialog')).findByRole('img', {
        name: 'Photo, 2023, 1 of 1',
      }),
    ).toBeVisible();
  });

  it('announces a new year group politely, and not each memory', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const announcer = within(story()).getByRole('status');
    expect(announcer).toHaveTextContent('1 year ago · 2025');

    await user().click(button('Next'));
    expect(within(story()).getByRole('status')).toBe(announcer);
    expect(announcer).toHaveTextContent('1 year ago · 2025');
    await user().click(button('Next'));
    expect(announcer).toHaveTextContent('3 years ago · 2023');
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

  it('stops reloading the day once the next change starts', async () => {
    await openStory('/day/2026-11-11?year=2025');
    const reload = hold();
    const reloads: AbortSignal[] = [];
    server.use(
      http.get('/api/v1/flashbacks/on-this-day', async ({ request }) => {
        reloads.push(request.signal);
        await reload.until;
        return HttpResponse.json({ date: '2026-11-11', years: withSaved(years) });
      }),
    );
    fireEvent.click(button('Star'));
    await waitFor(() => {
      expect(reloads).toHaveLength(1);
    });

    fireEvent.click(button('Like'));
    await advance(0);

    expect(reloads[0]?.aborted).toBe(true);
    reload.release();
  });

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
    expect(within(toast()).getByRole('status')).toHaveTextContent("Couldn't save");
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
    expect(within(toast()).getByRole('status')).toHaveTextContent("Couldn't save");
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
    const { router } = await openStory('/day/2026-11-11?year=2025');
    await waitFor(() => {
      expect(router.options.context.queryClient.isFetching()).toBe(0);
    });
    let reload: (response: Response) => void = () => undefined;
    let recentDays: (days: ReturnType<typeof threeYears>) => void = () => undefined;
    const recentAnswered = new Promise<ReturnType<typeof threeYears>>((resolve) => {
      recentDays = resolve;
    });
    let reloading = false;
    server.use(
      onThisDayAnswers((date) =>
        date === '2026-11-11' && !reloading
          ? new Promise<Response>((resolve) => {
              reloading = true;
              reload = resolve;
            })
          : date === '2026-11-11'
            ? withSaved(years)
            : [],
      ),
      recentDaysAnswers(async (window) => {
        const days = await recentAnswered;
        return recentDaysOf((date) => (date === '2026-11-10' ? days : []))(window);
      }),
    );
    act(() => {
      becomes('visible');
    });
    await advance(0);

    fireEvent.click(button('Star'));
    await advance(0);
    reload(Response.json({ date: '2026-11-11', years: threeYears() }));
    recentDays([yearGroup(2024, memory(ids.a2025, 2024))]);
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
    await waitFor(() => {
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
    const first = () => todayCard().querySelector('[data-shown] img')?.getAttribute('src');
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });
    const respond = later();
    fireEvent.click(button('Hide'));
    fireEvent.click(button('Close'));
    await advance(0);
    expect(first()).toContain(ids.a2025);

    respond();
    await advance(0);

    await waitFor(() => {
      expect(first()).toContain(ids.b2025);
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
    await advance(1000);

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
    expect(within(toast()).getByRole('status')).toHaveTextContent('Memory hidden');
    expect(within(toast()).getByRole('button', { name: 'Undo' })).toBeVisible();
    fireEvent.click(button('Previous'));
    expect(showing()).toBe(preview(ids.b2025));
    await advance(4999);
    expect(screen.queryByRole('alertdialog')).not.toBeNull();
    await advance(1);
    expect(screen.queryByRole('alertdialog')).toBeNull();
  });

  it('gives the memory after a hidden one its own clock', async () => {
    previewsNotPrefetched();
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(4000);

    fireEvent.click(button('Hide'));
    await advance(4000);
    expect(await shown()).toBe(preview(ids.b2025));
    await advance(4999);
    expect(showing()).toBe(preview(ids.b2025));
    await advance(1);
    expect(await shown()).toBe(preview(ids.a2023));
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
    expect(await shown()).toBe(preview(ids.b2025));
    await swipe(-60, 0);
    expect(showing()).toBe(preview(ids.a2021));
    await swipe(60, 0);
    expect(showing()).toBe(preview(ids.a2025));
    fireEvent.click(within(toast()).getByRole('button', { name: 'Undo' }));
    expect(showing()).toBe(preview(ids.a2023));
    await waitFor(() => {
      expect(requested).toHaveLength(3);
    });
    expect(sent).toEqual([
      { id: ids.a2023, body: { hidden: true } },
      { id: ids.a2023, body: { hidden: false } },
    ]);
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
    expect(within(toast()).getByRole('status')).toHaveTextContent("Couldn't save");
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
    expect(within(toast()).getByRole('status')).toHaveTextContent("Couldn't save");
  });

  it("drops a memory whose Undo couldn't be saved without moving on from where the story is", async () => {
    previewsNotPrefetched();
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
    expect(await shown()).toBe(preview(ids.a2023));
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
        memory(ids.a2025, 2025, {
          proxies: { thumbnail: null, preview: null, compactPreview: null, video: null },
        }),
      ),
    ];
    const { router } = await openLanding();
    fireEvent.click(todayCard());
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

describe('a burst', () => {
  const burstId = '0199a1b2-c3d4-7e5f-8a9b-0000000e2025';
  const frameIds = [
    ids.a2025,
    '0199a1b2-c3d4-7e5f-8a9b-0000000f2025',
    '0199a1b2-c3d4-7e5f-8a9b-0000000c2025',
  ];
  const frame = (id: string, index: number) =>
    memory(id, 2025, { burst: { burstId, isCover: index === 0, count: frameIds.length } });
  let opened: string[];
  let sent: Interacted[];
  let refuse: boolean;

  beforeEach(() => {
    opened = [];
    sent = [];
    refuse = false;
    years = [
      yearGroup(2025, frame(ids.a2025, 0), memory(ids.b2025, 2025)),
      yearGroup(2023, memory(ids.a2023, 2023)),
    ];
    server.use(
      burstAnswers((id) => {
        opened.push(id);
        const hidden = sent.filter(({ body }) => (body as { hidden?: boolean }).hidden);
        return frameIds.map(frame).filter((shown) => !hidden.some(({ id }) => id === shown.id));
      }),
      interactionAnswers(sent, () => (refuse ? problemResponse(500, 'unknown') : undefined)),
    );
  });

  const details = () => screen.getByRole('dialog', { name: 'Burst frames' });
  const counter = () => within(details()).getByText(/^\d+ \/ \d+$/);
  const frameShown = () =>
    within(details())
      .getAllByRole('img')
      .map((img) => img.getAttribute('aria-label'));

  async function openDetails() {
    await user().click(middle());
    return await screen.findByRole('img', { name: 'Frame 1 of 3' });
  }

  const frameSources = () =>
    [...details().querySelectorAll('img')].map((image) => image.getAttribute('src'));

  async function framesArrived() {
    await waitFor(() => {
      expect(frameSources()).toHaveLength(frameShown().length);
    });
    return frameSources().map(fromMemory);
  }

  it('opens its frames from the cover on a middle tap, with the story paused', async () => {
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(1000);

    await openDetails();

    expect(opened).toEqual([burstId]);
    expect(counter()).toHaveTextContent('1 / 3');
    expect(frameShown()).toEqual(['Frame 1 of 3', 'Frame 2 of 3', 'Frame 3 of 3']);
    expect(await framesArrived()).toEqual(frameIds.map(preview));
    expect(await axeResults(container)).toHaveNoViolations();
    await advance(30_000);
    expect(details()).toBeVisible();
    expect(document.querySelector('ol > li > span')).toHaveStyle({
      animationPlayState: 'paused',
    });
  });

  it('shows the compact preview of each frame on a phone that decodes JPEG XL', async () => {
    server.use(burstAnswers(() => frameIds.map((id, index) => withCompact(frame(id, index)))));
    await openStory('/day/2026-11-11?year=2025', phone());
    await shown();

    await openDetails();

    expect(await framesArrived()).toEqual(frameIds.map(compact));
  });

  it('closes on Esc and plays on from where the story paused', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await advance(1000);
    await openDetails();
    await advance(30_000);

    await user().keyboard('{Escape}');

    expect(screen.queryByRole('dialog', { name: 'Burst frames' })).toBeNull();
    expect(story()).toBeVisible();
    expect(segment(0)).toHaveStyle({ animationPlayState: 'running' });
    await advance(3999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
  });

  const frames = () => details().querySelector('ul') as HTMLUListElement;
  const detailsButton = (name: string) => within(details()).getByRole('button', { name });

  function scrolled(left: number, width = 390) {
    Object.defineProperty(frames(), 'clientWidth', { value: width, configurable: true });
    Object.defineProperty(frames(), 'scrollLeft', { value: left, configurable: true });
    fireEvent.scroll(frames());
  }

  async function tap(clientX: number) {
    await user().pointer({ keys: '[MouseLeft]', target: frames(), coords: { clientX } });
  }

  async function opened3() {
    const rendered = await openStory('/day/2026-11-11?year=2025');
    await shown();
    await openDetails();
    return rendered;
  }

  it('opens again on the frame now its cover, once that is loaded', async () => {
    renderApp('/');
    fireEvent.click(await screen.findByRole('link', { name: landingName }));
    await screen.findByRole('dialog', { name: storyName });
    await shown();
    await openDetails();
    await user().click(detailsButton('Hide'));
    await user().keyboard('{Escape}');
    fireEvent.click(button('Close'));
    await advance(5000);
    expect(screen.queryByRole('dialog', { name: storyName })).toBeNull();
    let answerCover: (answer: undefined) => void = () => undefined;
    const received: string[] = [];
    server.use(
      proxyAnswers(received, (url) =>
        url === preview(frameIds[1] as string)
          ? new Promise<undefined>((resolve) => {
              answerCover = resolve;
            })
          : undefined,
      ),
    );

    fireEvent.click(todayCard());
    await waitFor(() => {
      expect(received).toContain(preview(frameIds[1] as string));
    });

    expect(screen.queryByRole('dialog', { name: storyName })).toBeNull();
    answerCover(undefined);
    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
  });

  it('moves a frame with the arrow keys, stops at either end, and scrolls to it', async () => {
    await opened3();
    Object.defineProperty(frames(), 'clientWidth', { value: 390, configurable: true });
    const scrolledTo = vi.spyOn(Element.prototype, 'scrollLeft', 'set');

    await user().keyboard('{ArrowLeft}');
    expect(counter()).toHaveTextContent('1 / 3');
    await user().keyboard('{ArrowRight}');
    expect(counter()).toHaveTextContent('2 / 3');
    await user().keyboard('{ArrowRight}{ArrowRight}{ArrowRight}');
    expect(counter()).toHaveTextContent('3 / 3');
    await user().keyboard('{ArrowLeft}');

    expect(counter()).toHaveTextContent('2 / 3');
    expect(scrolledTo.mock.calls).toEqual([[0], [390], [780], [780], [780], [390]]);
  });

  it('moves a frame on a tap on its left or right half', async () => {
    await opened3();

    await tap(900);
    expect(counter()).toHaveTextContent('2 / 3');
    await tap(900);
    await tap(900);
    expect(counter()).toHaveTextContent('3 / 3');
    await tap(100);
    expect(counter()).toHaveTextContent('2 / 3');
    await tap(511);
    expect(counter()).toHaveTextContent('1 / 3');
    await tap(512);
    expect(counter()).toHaveTextContent('2 / 3');
  });

  it('moves a frame from its Previous frame and Next frame buttons', async () => {
    await opened3();

    await user().click(detailsButton('Next frame'));
    await user().click(detailsButton('Next frame'));
    expect(counter()).toHaveTextContent('3 / 3');
    await user().click(detailsButton('Previous frame'));

    expect(counter()).toHaveTextContent('2 / 3');
  });

  it('counts the frame a swipe scrolls to, to the nearest one', async () => {
    await opened3();

    scrolled(700);
    expect(counter()).toHaveTextContent('3 / 3');
    scrolled(580);
    expect(counter()).toHaveTextContent('2 / 3');
    scrolled(100, 0);

    expect(counter()).toHaveTextContent('2 / 3');
  });

  it('never moves on by itself', async () => {
    await opened3();

    await advance(60_000);

    expect(counter()).toHaveTextContent('1 / 3');
  });

  it('leaves the story where it was while the frames are open', async () => {
    await opened3();

    await user().keyboard('{Shift>}{ArrowRight}{/Shift}{ArrowRight} m');
    await user().keyboard('{Escape}');

    expect(showing()).toBe(preview(ids.a2025));
    expect(button('Pause')).toBeVisible();
    await advance(5000);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('stays paused once closed when the story was paused before', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Pause'));
    await openDetails();

    await user().click(detailsButton('Close'));

    expect(screen.queryByRole('dialog', { name: 'Burst frames' })).toBeNull();
    expect(button('Play')).toBeVisible();
    await advance(10_000);
    expect(showing()).toBe(preview(ids.a2025));
  });

  it('closes on a swipe down, back to the story', async () => {
    await opened3();

    await swipe(0, 300, frames());

    expect(screen.queryByRole('dialog', { name: 'Burst frames' })).toBeNull();
    expect(showing()).toBe(preview(ids.a2025));
    await advance(5000);
    expect(showing()).toBe(preview(ids.b2025));
  });

  it('neither closes nor moves a frame on a short swipe down', async () => {
    await opened3();
    await user().keyboard('{ArrowRight}');

    await swipe(0, 100, frames(), 500);

    expect(counter()).toHaveTextContent('2 / 3');
    await tap(900);
    expect(counter()).toHaveTextContent('3 / 3');
  });

  it("opens on Enter, but not on Enter on one of the story's buttons", async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    act(() => {
      button('Pause').focus();
    });

    await user().keyboard('{Enter}');
    expect(button('Play')).toBeVisible();
    expect(screen.queryByRole('dialog', { name: 'Burst frames' })).toBeNull();
    act(() => {
      story().focus();
    });
    await user().keyboard('{Enter}');

    expect(await screen.findByRole('dialog', { name: 'Burst frames' })).toBeVisible();
  });

  it('moves on from a photo on Enter', async () => {
    years = [yearGroup(2025, memory(ids.a2025, 2025), memory(ids.b2025, 2025))];
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    act(() => {
      story().focus();
    });

    await user().keyboard('{Enter}');

    expect(showing()).toBe(preview(ids.b2025));
  });

  const toast = () => screen.getByRole('alertdialog');

  describe('its frames', () => {
    it.each([
      ['Star', 'starred'],
      ['Like', 'liked'],
    ])('%s each frame on its own', async (name, flag) => {
      await opened3();
      await user().keyboard('{ArrowRight}');

      await user().click(detailsButton(name));
      await advance(0);

      expect(detailsButton(name)).toHaveAttribute('aria-pressed', 'true');
      await user().keyboard('{ArrowLeft}');
      expect(detailsButton(name)).toHaveAttribute('aria-pressed', 'false');
      expect(sent).toEqual([{ id: frameIds[1], body: { [flag]: true } }]);
    });

    it('takes a hidden frame out at once, and Undo puts it back', async () => {
      const { container } = await opened3();
      await user().keyboard('{ArrowRight}');

      await user().click(detailsButton('Hide'));

      expect(frameShown()).toEqual(['Frame 1 of 2', 'Frame 2 of 2']);
      expect(counter()).toHaveTextContent('2 / 2');
      expect((await framesArrived())[1]).toBe(preview(frameIds[2] as string));
      expect(toast()).toHaveTextContent('Memory hidden');
      expect(await axeResults(container)).toHaveNoViolations();
      await user().click(within(toast()).getByRole('button', { name: 'Undo' }));
      await advance(0);

      expect(frameShown()).toEqual(['Frame 1 of 3', 'Frame 2 of 3', 'Frame 3 of 3']);
      expect(sent).toEqual([
        { id: frameIds[1], body: { hidden: true } },
        { id: frameIds[1], body: { hidden: false } },
      ]);
    });

    it('puts a frame back whose hide could not be saved', async () => {
      await opened3();
      refuse = true;

      await user().click(detailsButton('Hide'));
      await advance(0);

      expect(frameShown()).toEqual(['Frame 1 of 3', 'Frame 2 of 3', 'Frame 3 of 3']);
      expect(toast()).toHaveTextContent("Couldn't save");
    });

    it('makes the next frame the cover when the cover is hidden', async () => {
      await opened3();

      await user().click(detailsButton('Hide'));
      await user().keyboard('{Escape}');
      await advance(0);

      expect(showing()).toBe(preview(frameIds[1] as string));
      expect(within(story()).getByText('+1')).toBeVisible();
      expect(sent).toEqual([{ id: ids.a2025, body: { hidden: true } }]);
    });

    it('hides the card when its last frame is hidden, and Undo brings it back', async () => {
      await opened3();
      await user().click(detailsButton('Hide'));
      await user().click(detailsButton('Hide'));

      await user().click(detailsButton('Hide'));

      expect(screen.queryByRole('dialog', { name: 'Burst frames' })).toBeNull();
      expect(showing()).toBe(preview(ids.b2025));
      await user().click(
        within(screen.getAllByRole('alertdialog')[0] as HTMLElement).getByRole('button', {
          name: 'Undo',
        }),
      );
      await advance(0);
      expect(await shown()).toBe(preview(frameIds[2] as string));
      expect(within(story()).queryByText(/^\+/)).toBeNull();
      expect(sent).toHaveLength(4);
      expect(sent).toEqual(
        expect.arrayContaining([
          { id: frameIds[0], body: { hidden: true } },
          { id: frameIds[1], body: { hidden: true } },
          { id: frameIds[2], body: { hidden: true } },
          { id: frameIds[2], body: { hidden: false } },
        ]),
      );
    });
  });

  it('keeps the card on Hide, with its next frame as the cover', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().click(button('Hide'));
    await advance(0);

    expect(opened).toEqual([burstId]);
    expect(sent).toEqual([{ id: ids.a2025, body: { hidden: true } }]);
    expect(await shown()).toBe(preview(frameIds[1] as string));
    expect(within(story()).getByText('+1')).toBeVisible();
    await user().click(within(toast()).getByRole('button', { name: 'Undo' }));
    await advance(0);
    expect(showing()).toBe(preview(ids.a2025));
    expect(within(story()).getByText('+2')).toBeVisible();
  });

  it("says Couldn't save and hides nothing when its frames do not load on Hide", async () => {
    server.use(burstAnswers(() => problemResponse(500, 'unknown')));
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Pause'));

    await user().click(button('Hide'));
    await advance(8000);

    expect(toast()).toHaveTextContent("Couldn't save");
    expect(sent).toEqual([]);
    expect(showing()).toBe(preview(ids.a2025));
    expect(within(story()).getByText('+2')).toBeVisible();
  });

  it('keeps its new cover when the story comes back to it much later', async () => {
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(button('Hide'));
    await advance(0);
    await user().click(button('Pause'));
    await user().click(button('Next'));

    await advance(60 * 60_000);
    await user().click(button('Previous'));

    expect(await shown()).toBe(preview(frameIds[1] as string));
    expect(within(story()).getByText('+1')).toBeVisible();
  });

  it('keeps a tapped year busy when its cover changed to a frame whose thumbnail was not shown', async () => {
    const newCover = preview(frameIds[1] as string);
    const { handler, release: answerCover } = holdProxy(newCover);
    server.use(handler);
    await openLanding();
    fireEvent.click(todayCard());
    await screen.findByRole('dialog', { name: storyName });
    await user().click(button('Pause'));
    await user().click(button('Hide'));
    await advance(0);
    await user().click(button('Close'));
    await screen.findByRole('link', { name: landingName });

    fireEvent.click(todayCard());
    await advance(5000);

    expect(screen.queryByRole('dialog')).toBeNull();
    answerCover();
    expect(await screen.findByRole('dialog', { name: storyName })).toBeVisible();
    expect(await shown()).toBe(newCover);
  });

  it('keeps a burst query only for a memory that is a burst', async () => {
    const { router } = await openStory('/day/2026-11-11?year=2025');
    await shown();
    await openDetails();
    await user().keyboard('{Escape}');
    fireEvent.click(button('Next'));
    await shown();

    const bursts = router.options.context.queryClient
      .getQueryCache()
      .findAll({ queryKey: ['burst'] })
      .map(({ queryKey }) => queryKey);
    expect(bursts).toEqual([['burst', burstId]]);
  });

  it('downloads each frame preview once, and not the cover the story already holds', async () => {
    const received: string[] = [];
    server.use(proxyAnswers(received));
    await opened3();
    await framesArrived();
    await user().keyboard('{Escape}');
    await openDetails();
    await framesArrived();

    const framePreviews = frameIds.map(preview);
    expect(received.filter((url) => framePreviews.includes(url))).toEqual(framePreviews);
  });

  it('shows a frame from the network when its download fails', async () => {
    const failing = preview(frameIds[1] as string);
    server.use(
      proxyAnswers([], (url) =>
        url === failing ? new HttpResponse(null, { status: 500 }) : undefined,
      ),
    );
    await opened3();

    await waitFor(() => {
      expect(frameSources()).toEqual([
        `memory:${preview(ids.a2025)}`,
        failing,
        `memory:${preview(frameIds[2] as string)}`,
      ]);
    });
  });

  it("shows each frame's blurhash until its picture shows", async () => {
    const blurhash = 'LEHV6nWB2yk8pyo0adR*.7kCMdnj';
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
      createImageData: (width: number, height: number) => ({
        width,
        height,
        data: new Uint8ClampedArray(width * height * 4),
      }),
      putImageData: () => undefined,
    } as unknown as CanvasRenderingContext2D);
    server.use(
      burstAnswers(() => frameIds.map((id, index) => ({ ...frame(id, index), blurhash }))),
    );
    await opened3();
    await framesArrived();
    const canvases = () => details().querySelectorAll('canvas');
    expect(canvases()).toHaveLength(3);

    fireEvent.load(details().querySelectorAll('img')[1] as HTMLImageElement);
    await advance(0);

    expect(canvases()).toHaveLength(2);
    expect(
      within(details()).getByRole('img', { name: 'Frame 2 of 3' }).querySelector('canvas'),
    ).toBeNull();
  });

  it('moves on from a card on Hide when it has one frame left', async () => {
    years = [
      yearGroup(
        2025,
        memory(ids.a2025, 2025, { burst: { burstId, isCover: true, count: 1 } }),
        memory(ids.b2025, 2025),
      ),
    ];
    await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().click(button('Hide'));
    await advance(0);

    expect(showing()).toBe(preview(ids.b2025));
    expect(opened).toEqual([]);
    expect(sent).toEqual([{ id: ids.a2025, body: { hidden: true } }]);
  });

  it('shows its cover while its frames load, and Try again when they fail', async () => {
    const answered = hold();
    let fails = true;
    server.use(
      burstAnswers(async () => {
        await answered.until;
        return fails ? problemResponse(500, 'unknown') : frameIds.map(frame);
      }),
    );
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();

    await user().click(middle());
    await screen.findByRole('dialog', { name: 'Burst frames' });
    expect(frameShown()).toEqual(['Frame 1 of 1']);
    expect(within(details()).queryByText(/^\d+ \/ \d+$/)).toBeNull();
    answered.release();
    await advance(8000);
    await within(details()).findByText("Couldn't load these frames");
    expect(await axeResults(container)).toHaveNoViolations();
    fails = false;
    await user().click(detailsButton('Try again'));

    expect(await within(details()).findByText('1 / 3')).toBeVisible();
    expect(within(details()).queryByText("Couldn't load these frames")).toBeNull();
  });

  it('keeps loading its frames through a star given while they load', async () => {
    const answered = hold();
    server.use(
      burstAnswers(async () => {
        await answered.until;
        return frameIds.map(frame);
      }),
    );
    await openStory('/day/2026-11-11?year=2025');
    await shown();
    await user().click(middle());
    await screen.findByRole('dialog', { name: 'Burst frames' });

    await user().click(detailsButton('Star'));
    answered.release();

    expect(await within(details()).findByText('1 / 3')).toBeVisible();
  });

  it('shows its cover with +2 for its other frames for 5 s, as one memory', async () => {
    const { container } = await openStory('/day/2026-11-11?year=2025');
    await shown();

    expect(within(story()).getByRole('img', { name: 'Burst, 2025, 1 of 2' })).toBeVisible();
    expect(showing()).toBe(preview(ids.a2025));
    expect(within(story()).getByText('+2')).toBeVisible();
    await advance(4999);
    expect(showing()).toBe(preview(ids.a2025));
    await advance(1);
    expect(showing()).toBe(preview(ids.b2025));
    expect(within(story()).queryByText('+2')).toBeNull();
    expect(opened).toEqual([]);
    await user().click(button('Previous'));
    expect(await axeResults(container)).toHaveNoViolations();
  });
});

describe('on a phone', () => {
  it('shows and fetches ahead the compact preview of each memory that has one', async () => {
    const received: string[] = [];
    server.use(proxyAnswers(received));
    years = [yearGroup(2025, withCompact(memory(ids.a2025, 2025)), memory(ids.b2025, 2025))];

    await openStory('/day/2026-11-11?year=2025', phone());

    expect(await shown()).toBe(compact(ids.a2025));
    await waitFor(() => {
      expect(received).toContain(preview(ids.b2025));
    });
    expect(received).not.toContain(preview(ids.a2025));
  });

  it('shows the preview on a phone that cannot decode JPEG XL', async () => {
    years = [yearGroup(2025, withCompact(memory(ids.a2025, 2025)))];

    await openStory('/day/2026-11-11?year=2025', fakeDisplay({ jpegXl: false, longEdge: 844 }));

    expect(await shown()).toBe(preview(ids.a2025));
  });
});
