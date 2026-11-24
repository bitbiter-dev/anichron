import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { beforeEach, describe, expect, it } from 'vitest';
import { axeResults } from '../../test/axe';
import { refreshSucceeds } from '../../test/handlers/auth';
import { hold, proxyPath, proxyUrl } from '../../test/handlers/flashbacks';
import { removedMemory, softDeletedAnswers } from '../../test/handlers/soft-deleted';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { advance, user } from '../../test/time';

const seen = (year: number, month: number, day: number) =>
  new Date(year, month - 1, day, 12).toISOString();
const id = (n: number) => `0199a1b2-c3d4-7e5f-8a9b-${String(n).padStart(12, '0')}`;

type Item = ReturnType<typeof removedMemory>;

function removed(n: number, overrides: Partial<Item> = {}) {
  return removedMemory(id(n), `2019/Trip/IMG_${String(n).padStart(4, '0')}.HEIC`, {
    lastSeenOnNas: seen(2026, 11, 3),
    ...overrides,
  });
}

const name = (n: number) => `IMG_${String(n).padStart(4, '0')}.HEIC`;

function onePage(...items: Item[]) {
  return softDeletedAnswers(() => ({ items, next: null }));
}

let asked: (string | null)[];

function pages(...all: Item[][]) {
  return softDeletedAnswers((after) => {
    asked.push(after);
    const index = after === null ? 0 : Number(after.slice(4));
    return {
      items: all[index] ?? [],
      next: index + 1 < all.length ? `page${String(index + 1)}` : null,
    };
  });
}

const fails = () => HttpResponse.json({ status: 500 }, { status: 500 });

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
  asked = [];
  server.use(refreshSucceeds());
});

const row = (n: number) =>
  screen.getByRole('button', { name: new RegExp(name(n).replace('.', '\\.')) });
const rowNames = () =>
  within(screen.getByRole('list'))
    .getAllByRole('button')
    .map((button) => button.textContent);
const viewer = () => screen.getByRole('dialog', { name: 'Removed memories' });
const caption = () => within(viewer()).getByText(/^2019\/Trip\//).parentElement as HTMLElement;

async function openRemoved() {
  const rendered = renderApp('/removed');
  await screen.findByText(name(1));
  return rendered;
}

describe('finding the screen', () => {
  it('is linked from the Library card in Settings, beside Hidden memories, and goes back to it', async () => {
    server.use(onePage(removed(1)));
    renderApp('/settings');

    const library = await screen.findByRole('region', { name: 'Library' });
    expect(
      within(library)
        .getAllByRole('link')
        .map((link) => link.textContent),
    ).toEqual(['Hidden memories', 'Removed memories']);
    await user().click(within(library).getByRole('link', { name: 'Removed memories' }));

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Removed memories' }),
    ).toBeVisible();
    expect(await screen.findByText(name(1))).toBeVisible();
    await user().click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { level: 1, name: 'Settings' })).toBeVisible();
  });
});

describe('the list', () => {
  it('lists each memory by its file name over its folder, with why it went and when', async () => {
    server.use(
      onePage(
        removed(1, { reason: 'Missing', lastSeenOnNas: seen(2026, 11, 3) }),
        removed(2, { reason: 'Overwritten', lastSeenOnNas: seen(2026, 10, 28) }),
        removed(3, { reason: 'Superseded', lastSeenOnNas: seen(2025, 2, 1) }),
      ),
    );
    const { container } = await openRemoved();

    expect(rowNames()).toEqual([
      `${name(1)}2019/TripNot found on your NAS · last seen Nov 3, 2026`,
      `${name(2)}2019/TripAnother file took its place · last seen Oct 28, 2026`,
      `${name(3)}2019/TripIts Live Photo has a newer video · removed around Feb 1, 2025`,
    ]);
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('shows a file at the top of the storage folder without a folder', async () => {
    server.use(onePage(removedMemory(id(1), name(1))));
    await openRemoved();

    expect(row(1)).toHaveTextContent(`${name(1)}Not found on your NAS`);
  });

  it('shows each thumbnail, a Live Photo with its badge, and a video with no thumbnail as a video glyph', async () => {
    const noProxies = { thumbnail: null, preview: null, compactPreview: null, video: null };
    server.use(
      onePage(
        removed(1),
        removed(2, {
          mediaType: 'LivePhoto',
          livePhotoVideoUrl: proxyPath(id(20), 'video'),
        }),
        removed(3, {
          mediaType: 'Video',
          reason: 'Superseded',
          proxies: { ...noProxies, video: proxyPath(id(3), 'video') },
        }),
        removed(4, {
          mediaType: 'Video',
          proxies: { ...noProxies, thumbnail: proxyPath(id(4), 'thumbnail') },
        }),
        removed(5, { proxies: noProxies }),
      ),
    );
    await openRemoved();

    const tile = (n: number) => within(row(n)).getByRole('img');
    expect(tile(1)).toHaveAccessibleName('Photo');
    expect(tile(1).querySelector('img')).toHaveAttribute('src', proxyUrl(id(1), 'thumbnail'));
    expect(tile(1).querySelector('svg')).toBeNull();
    expect(tile(2)).toHaveAccessibleName('Live Photo');
    expect(tile(2).querySelector('svg')).not.toBeNull();
    expect(tile(3)).toHaveAccessibleName('Video');
    expect(tile(3).querySelector('svg')).not.toBeNull();
    expect(tile(3).querySelector('img')).toBeNull();
    expect(tile(4).querySelector('img')).toHaveAttribute('src', proxyUrl(id(4), 'thumbnail'));
    expect(tile(4).querySelector('svg')).toBeNull();
    expect(tile(5).querySelector('svg')).toBeNull();
  });

  it('loads the list afresh when the screen opens again', async () => {
    let items = [removed(1)];
    server.use(softDeletedAnswers(() => ({ items, next: null })));
    const { router } = await openRemoved();

    items = [removed(2), removed(1)];
    const { queryClient } = router.options.context;
    queryClient.setQueryData(['on-this-day', '2026-11-18'], { years: [] });
    await act(() => router.navigate({ to: '/settings' }));
    expect(queryClient.getQueryData(['on-this-day', '2026-11-18'])).toEqual({ years: [] });
    await act(() => router.navigate({ to: '/removed' }));

    await waitFor(() => {
      expect(rowNames().map((text) => text.slice(0, name(1).length))).toEqual([name(2), name(1)]);
    });
  });

  it('says Nothing removed, with what shows up here, when there are none', async () => {
    server.use(onePage());
    const { container } = renderApp('/removed');

    expect(await screen.findByText('Nothing removed')).toBeVisible();
    expect(
      screen.getByText(
        'Memories whose file went missing or was replaced on your NAS show up here.',
      ),
    ).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('is busy with skeleton rows while the first page loads', async () => {
    const answer = hold();
    server.use(
      softDeletedAnswers(async () => {
        await answer.until;
        return { items: [removed(1)], next: null };
      }),
    );
    renderApp('/removed');

    const main = await screen.findByRole('main');
    await advance(1000);
    expect(main).toHaveAttribute('aria-busy', 'true');
    expect(main.querySelectorAll('.skeleton').length).toBeGreaterThan(0);
    answer.release();
    expect(await screen.findByText(name(1))).toBeVisible();
    expect(main).toHaveAttribute('aria-busy', 'false');
    expect(main.querySelector('.skeleton')).toBeNull();
  });

  it('offers Try again when the list cannot load', async () => {
    let failing = true;
    server.use(softDeletedAnswers(() => (failing ? fails() : { items: [removed(1)], next: null })));
    renderApp('/removed');
    await advance(10_000);

    expect(await screen.findByText("Couldn't load your removed memories.")).toBeVisible();
    failing = false;
    await user().click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText(name(1))).toBeVisible();
  });

  it('loads the next page as the list scrolls near its end, with a skeleton row meanwhile, and stops after the last', async () => {
    pageHeight(4000);
    const next = hold();
    server.use(
      softDeletedAnswers(async (after) => {
        asked.push(after);
        if (after === null) return { items: [removed(1), removed(2)], next: 'page1' };
        await next.until;
        return { items: [removed(3)], next: null };
      }),
    );
    await openRemoved();
    expect(asked).toEqual([null]);

    scrollTo(2431);
    await advance(0);
    expect(asked).toEqual([null]);

    scrollTo(2432);
    await advance(0);
    const main = screen.getByRole('main');
    expect(screen.getByRole('list').nextElementSibling?.querySelector('.skeleton')).not.toBeNull();
    next.release();
    expect(await screen.findByText(name(3))).toBeVisible();
    expect(main.querySelector('.skeleton')).toBeNull();
    scrollTo(2700);
    await advance(0);

    expect(asked).toEqual([null, 'page1']);
  });

  it('offers Try again at the end when the next page cannot load, and does not retry on its own', async () => {
    pageHeight(4000);
    let failing = true;
    server.use(
      softDeletedAnswers((after) => {
        asked.push(after);
        if (after === null) return { items: [removed(1)], next: 'page1' };
        return failing ? fails() : { items: [removed(2)], next: null };
      }),
    );
    await openRemoved();
    scrollTo(2432);
    await advance(10_000);

    expect(await screen.findByText("Couldn't load more removed memories.")).toBeVisible();
    expect(screen.getByText(name(1))).toBeVisible();
    const tries = asked.length;
    scrollTo(2600);
    await advance(10_000);
    expect(asked).toHaveLength(tries);

    failing = false;
    await user().click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByText(name(2))).toBeVisible();
    expect(screen.queryByText("Couldn't load more removed memories.")).toBeNull();
  });
});

describe('the viewer', () => {
  async function openViewer(n: number) {
    await user().click(row(n));
    return viewer();
  }

  it('opens the tapped memory full screen, captioned with its path and why it went', async () => {
    server.use(onePage(removed(1), removed(2, { reason: 'Overwritten' }), removed(3)));
    const { container } = await openRemoved();

    await openViewer(2);

    expect(caption()).toHaveTextContent(
      `2019/Trip/${name(2)}Another file took its place · last seen Nov 3, 2026`,
    );
    expect(within(viewer()).getAllByRole('img', { name: 'Photo' })).toHaveLength(3);
    expect(await axeResults(container.ownerDocument.body)).toHaveNoViolations();
  });

  it('moves with the arrow keys and the Previous and Next buttons, stopping at either end', async () => {
    server.use(onePage(removed(1), removed(2), removed(3)));
    await openRemoved();
    await openViewer(2);

    await user().keyboard('{ArrowRight}');
    expect(caption()).toHaveTextContent(name(3));
    await user().keyboard('{ArrowRight}');
    expect(caption()).toHaveTextContent(name(3));
    await user().click(within(viewer()).getByRole('button', { name: 'Previous memory' }));
    await user().click(within(viewer()).getByRole('button', { name: 'Previous memory' }));
    await user().click(within(viewer()).getByRole('button', { name: 'Previous memory' }));
    expect(caption()).toHaveTextContent(name(1));
    await user().click(within(viewer()).getByRole('button', { name: 'Next memory' }));
    expect(caption()).toHaveTextContent(name(2));
    await advance(60_000);
    expect(caption()).toHaveTextContent(name(2));
  });

  it('closes on Esc and on its close button, back to the row it opened', async () => {
    server.use(onePage(removed(1), removed(2)));
    await openRemoved();
    act(() => {
      row(2).focus();
    });
    await user().keyboard('{Enter}');
    expect(caption()).toHaveTextContent(name(2));

    await user().keyboard('{Escape}');
    await advance(100);
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(row(2)).toHaveFocus();

    await openViewer(1);
    await user().click(within(viewer()).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('offers no action: no star, like or hide', async () => {
    server.use(onePage(removed(1)));
    await openRemoved();
    await openViewer(1);

    expect(
      within(viewer())
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Previous memory', 'Next memory', '']);
  });

  it('fetches the next page as it nears the end of what is loaded', async () => {
    pageHeight(100_000);
    server.use(
      pages(
        [1, 2, 3, 4, 5].map((n) => removed(n)),
        [removed(6)],
      ),
    );
    await openRemoved();
    await openViewer(1);

    await user().keyboard('{ArrowRight}');
    await advance(0);
    expect(asked).toEqual([null]);
    await user().keyboard('{ArrowRight}');
    await advance(0);
    expect(asked).toEqual([null, 'page1']);
    await user().keyboard('{ArrowRight}{ArrowRight}{ArrowRight}');
    expect(await within(viewer()).findByText(`2019/Trip/${name(6)}`)).toBeVisible();
  });

  it('plays a superseded video without a poster', async () => {
    server.use(
      onePage(
        removed(1, {
          mediaType: 'Video',
          reason: 'Superseded',
          proxies: {
            thumbnail: null,
            preview: null,
            compactPreview: null,
            video: proxyPath(id(1), 'video'),
          },
        }),
      ),
    );
    await openRemoved();
    await openViewer(1);

    const video = viewer().querySelector('video') as HTMLVideoElement;
    expect(video).toHaveAttribute('src', proxyUrl(id(1), 'video'));
    expect(video).not.toHaveAttribute('poster');
  });
});
