import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { axeResults } from '../../test/axe';
import {
  loginRespondsWith,
  logoutSucceeds,
  miraSession,
  refreshSucceeds,
} from '../../test/handlers/auth';
import { hold, proxyPath, proxyUrl } from '../../test/handlers/flashbacks';
import { purgeAnswers, removedMemory, softDeletedAnswers } from '../../test/handlers/soft-deleted';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { signIn } from '../../test/session';
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
      total: all.flat().length,
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
  it('lists each memory by its file name, why it went and when, without its folder', async () => {
    server.use(
      onePage(
        removed(1, { reason: 'Missing', lastSeenOnNas: seen(2026, 11, 3) }),
        removed(2, { reason: 'Overwritten', lastSeenOnNas: seen(2026, 10, 28) }),
        removed(3, { reason: 'Superseded', lastSeenOnNas: seen(2025, 2, 1) }),
      ),
    );
    const { container } = await openRemoved();

    expect(rowNames()).toEqual([
      `${name(1)}Source file was removedLast seen Nov 3, 2026`,
      `${name(2)}Source file was overwrittenLast seen Oct 28, 2026`,
      `${name(3)}Live Photo got a newer videoRemoved around Feb 1, 2025`,
    ]);
    expect(within(row(1)).getByText(name(1))).toHaveClass('font-semibold');
    expect(await axeResults(container)).toHaveNoViolations();
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
    expect(tile(2).querySelector('svg')).toHaveClass('text-on-player', 'player-glass');
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

  it('shows a thumbnail over its BlurHash until it decodes, and loads a broken one again', async () => {
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
      createImageData: (width: number, height: number) => ({
        width,
        height,
        data: new Uint8ClampedArray(width * height * 4),
      }),
      putImageData: () => undefined,
    } as unknown as CanvasRenderingContext2D);
    server.use(onePage(removed(1, { blurhash: 'LEHV6nWB2yk8pyo0adR*.7kCMdnj' }), removed(2)));
    await openRemoved();

    const thumbnail = (n: number) =>
      within(row(n)).getByRole('img').querySelector('img') as HTMLImageElement;
    expect(row(1).querySelector('canvas')).not.toBeNull();
    fireEvent.load(thumbnail(1));
    await advance(0);
    expect(row(1).querySelector('canvas')).toBeNull();

    const broken = thumbnail(2);
    fireEvent.error(broken);
    await waitFor(() => {
      expect(thumbnail(2)).not.toBe(broken);
    });
    expect(thumbnail(2)).toHaveAttribute('src', proxyUrl(id(2), 'thumbnail'));
  });

  it('says Nothing removed, with what shows up here, when there are none', async () => {
    server.use(onePage());
    const { container } = renderApp('/removed');

    expect(await screen.findByText('Nothing removed')).toBeVisible();
    expect(
      screen.getByText(
        'Memories whose file went missing or was overwritten on your NAS show up here.',
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
      `2019/Trip/${name(2)}Source file was overwrittenLast seen Nov 3, 2026`,
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

  it('offers only Forget: no star, like or hide', async () => {
    server.use(onePage(removed(1)));
    await openRemoved();
    await openViewer(1);

    expect(
      within(viewer())
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Previous memory', 'Next memory', '', 'Forget']);
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

// ============================================================================
// Purge slice (#342)
// ============================================================================

const checkbox = (n: number) => screen.getByRole('checkbox', { name: name(n) });
const checkedRows = () =>
  screen
    .getAllByRole('checkbox')
    .filter((box) => box.getAttribute('aria-checked') === 'true')
    .map((box) => box.textContent.slice(0, name(1).length));
const topBar = () => screen.getByRole('banner');
const selectionBar = () => screen.queryByRole('button', { name: /^Forget \d+$/ });
const confirmation = () => screen.getByRole('alertdialog');
const toast = (text: string) => screen.findByText(text);

async function startSelecting() {
  await user().click(within(topBar()).getByRole('button', { name: 'Select' }));
}

async function forgetSelected() {
  await user().click(selectionBar() as HTMLElement);
  await user().click(within(confirmation()).getByRole('button', { name: 'Forget' }));
}

describe('selecting', () => {
  it('turns on from Select in the top bar, where Select all and Cancel take the place of Back and Select', async () => {
    server.use(onePage(removed(1), removed(2)));
    const { container } = await openRemoved();

    await startSelecting();

    expect(
      within(topBar())
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Select all', 'Cancel']);
    expect(checkbox(1)).not.toBeChecked();
    expect(selectionBar()).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();

    await user().click(within(topBar()).getByRole('button', { name: 'Cancel' }));
    expect(
      within(topBar())
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Back', 'Select']);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(row(1)).toBeVisible();
  });

  it('offers no Select while nothing is listed', async () => {
    server.use(onePage());
    renderApp('/removed');
    expect(await screen.findByText('Nothing removed')).toBeVisible();

    expect(within(topBar()).queryByRole('button', { name: 'Select' })).toBeNull();
  });

  it('toggles a row on tap, with Forget and the count at the bottom, announced as it changes', async () => {
    server.use(onePage(removed(1), removed(2), removed(3)));
    const { container } = await openRemoved();
    await startSelecting();

    await user().click(checkbox(1));
    await user().click(checkbox(3));
    expect(checkedRows()).toEqual([name(1), name(3)]);
    expect(selectionBar()).toHaveTextContent('Forget 2');
    expect(selectionBar()).toHaveClass('bg-accent', 'text-on-accent');
    expect(screen.getByText('2 memories selected')).toHaveAttribute('aria-live', 'polite');

    await user().click(checkbox(3));
    expect(checkedRows()).toEqual([name(1)]);
    expect(screen.getByText('1 memory selected')).toBeVisible();
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('selects every row at once when all are loaded, then offers Deselect all, which clears them', async () => {
    server.use(onePage(removed(1), removed(2), removed(3)));
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(2));

    await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
    expect(checkedRows()).toEqual([name(1), name(2), name(3)]);
    expect(screen.queryByRole('dialog')).toBeNull();

    await user().click(within(topBar()).getByRole('button', { name: 'Deselect all' }));
    expect(checkedRows()).toEqual([]);
    expect(selectionBar()).toBeNull();
    expect(within(topBar()).getByRole('button', { name: 'Select all' })).toBeVisible();
  });

  it('offers Deselect all once every row is selected by hand', async () => {
    server.use(onePage(removed(1), removed(2)));
    await openRemoved();
    await startSelecting();

    await user().click(checkbox(1));
    expect(within(topBar()).getByRole('button', { name: 'Select all' })).toBeVisible();
    await user().click(checkbox(2));
    expect(within(topBar()).getByRole('button', { name: 'Deselect all' })).toBeVisible();
  });

  describe('with more still to load', () => {
    const selectAllDialog = () => screen.getByRole('dialog', { name: /^Select all/ });

    beforeEach(() => {
      pageHeight(4000);
    });

    it('asks first, with the total and how many more need to load', async () => {
      const first = Array.from({ length: 50 }, (_, index) => removed(index + 1));
      server.use(softDeletedAnswers(() => ({ items: first, next: 'page1', total: 1240 })));
      const { container } = await openRemoved();
      await startSelecting();

      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));

      expect(selectAllDialog()).toHaveAccessibleName('Select all 1,240?');
      expect(selectAllDialog()).toHaveTextContent('1,190 more need to load first.');
      expect(
        within(selectAllDialog())
          .getAllByRole('button')
          .map((button) => button.textContent),
      ).toEqual(['Cancel', 'Load and select']);
      expect(await axeResults(container.ownerDocument.body)).toHaveNoViolations();
    });

    it('asks first on Ctrl+A too', async () => {
      server.use(pages([removed(1), removed(2)], [removed(3)]));
      await openRemoved();
      await startSelecting();

      fireEvent.keyDown(checkbox(1), { key: 'a', ctrlKey: true });

      expect(await screen.findByRole('dialog', { name: 'Select all 3?' })).toBeVisible();
    });

    it('says one more needs to load', async () => {
      server.use(pages([removed(1), removed(2)], [removed(3)]));
      await openRemoved();
      await startSelecting();

      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));

      expect(selectAllDialog()).toHaveAccessibleName('Select all 3?');
      expect(selectAllDialog()).toHaveTextContent('1 more needs to load first.');
    });

    it('changes nothing on Cancel', async () => {
      server.use(pages([removed(1), removed(2)], [removed(3)]));
      await openRemoved();
      await startSelecting();
      await user().click(checkbox(1));

      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
      await user().click(within(selectAllDialog()).getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('dialog')).toBeNull();
      expect(asked).toEqual([null]);
      expect(checkedRows()).toEqual([name(1)]);
    });

    it('loads every page on Load and select, then selects them all', async () => {
      server.use(pages([removed(1), removed(2)], [removed(3)], [removed(4)]));
      await openRemoved();
      await startSelecting();

      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
      await user().click(
        within(selectAllDialog()).getByRole('button', { name: 'Load and select' }),
      );

      await waitFor(() => {
        expect(checkedRows()).toEqual([name(1), name(2), name(3), name(4)]);
      });
      expect(screen.queryByRole('dialog')).toBeNull();
      expect(asked).toEqual([null, 'page1', 'page2']);
      expect(selectionBar()).toHaveTextContent('Forget 4');
      expect(within(topBar()).getByRole('button', { name: 'Deselect all' })).toBeVisible();
    });

    it('says so when a page fails, and keeps everything that loaded selected', async () => {
      server.use(
        softDeletedAnswers((after) => {
          asked.push(after);
          if (after === null) return { items: [removed(1)], next: 'page1', total: 3 };
          if (after === 'page1') return { items: [removed(2)], next: 'page2', total: 3 };
          return fails();
        }),
      );
      await openRemoved();
      await startSelecting();

      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
      await user().click(
        within(selectAllDialog()).getByRole('button', { name: 'Load and select' }),
      );
      await advance(10_000);

      expect(await screen.findByText("Couldn't load more removed memories.")).toBeVisible();
      expect(checkedRows()).toEqual([name(1), name(2)]);
      expect(asked).toContain('page2');
      expect(within(topBar()).getByRole('button', { name: 'Select all' })).toBeVisible();
    });

    it('waits for a page already loading instead of asking for it again', async () => {
      const next = hold();
      server.use(
        softDeletedAnswers(async (after) => {
          asked.push(after);
          if (after === null) return { items: [removed(1)], next: 'page1', total: 2 };
          await next.until;
          return { items: [removed(2)], next: null, total: 2 };
        }),
      );
      await openRemoved();
      await startSelecting();
      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
      scrollTo(4000);
      await advance(0);

      await user().click(
        within(selectAllDialog()).getByRole('button', { name: 'Load and select' }),
      );
      next.release();

      await waitFor(() => {
        expect(checkedRows()).toEqual([name(1), name(2)]);
      });
      expect(asked).toEqual([null, 'page1']);
    });

    it('selects nothing when select mode ends while the rest loads', async () => {
      const rest = hold();
      server.use(
        softDeletedAnswers(async (after) => {
          if (after === null) return { items: [removed(1)], next: 'page1', total: 2 };
          await rest.until;
          return { items: [removed(2)], next: null, total: 2 };
        }),
      );
      await openRemoved();
      await startSelecting();
      await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
      await user().click(
        within(selectAllDialog()).getByRole('button', { name: 'Load and select' }),
      );

      await user().click(within(topBar()).getByRole('button', { name: 'Cancel' }));
      rest.release();
      expect(await screen.findByText(name(2))).toBeVisible();
      await startSelecting();

      expect(checkedRows()).toEqual([]);
    });
  });

  it('starts afresh after Cancel', async () => {
    server.use(onePage(removed(1), removed(2)));
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));

    await user().click(within(topBar()).getByRole('button', { name: 'Cancel' }));
    await startSelecting();

    expect(checkedRows()).toEqual([]);
  });

  it('ends when the screen is left', async () => {
    server.use(onePage(removed(1)));
    const { router } = await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));

    await act(() => router.navigate({ to: '/settings' }));
    await act(() => router.navigate({ to: '/removed' }));

    expect(await screen.findByText(name(1))).toBeVisible();
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(within(topBar()).getByRole('button', { name: 'Back' })).toBeVisible();
  });

  it('keeps the rows still listed selected when the list loads again', async () => {
    let items = [removed(1), removed(2), removed(3)];
    server.use(softDeletedAnswers(() => ({ items, next: null })));
    const { router } = await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await user().click(checkbox(2));

    items = [removed(1), removed(3)];
    act(() => {
      void router.options.context.queryClient.invalidateQueries({ queryKey: ['removed'] });
    });

    await waitFor(() => {
      expect(screen.queryByText(name(2))).toBeNull();
    });
    expect(checkedRows()).toEqual([name(1)]);
    expect(selectionBar()).toHaveTextContent('Forget 1');
  });

  it('selects a lone row with Shift once the last toggled one has left the list', async () => {
    let items = [removed(1), removed(2), removed(3), removed(4)];
    server.use(softDeletedAnswers(() => ({ items, next: null })));
    const { router } = await openRemoved();
    await startSelecting();
    await user().click(checkbox(3));
    await user().click(checkbox(2));
    items = [removed(1), removed(3), removed(4)];
    act(() => {
      void router.options.context.queryClient.invalidateQueries({ queryKey: ['removed'] });
    });
    await waitFor(() => {
      expect(screen.queryByText(name(2))).toBeNull();
    });

    const typing = user();
    await typing.keyboard('{Shift>}');
    await typing.click(checkbox(4));
    await typing.keyboard('{/Shift}');

    expect(checkedRows()).toEqual([name(3), name(4)]);
  });

  describe('from the keyboard', () => {
    it('toggles the focused row with Space, and selects a range from the last toggled one with Shift', async () => {
      server.use(onePage(...[1, 2, 3, 4, 5].map((n) => removed(n))));
      await openRemoved();
      await startSelecting();
      act(() => {
        checkbox(1).focus();
      });

      await user().keyboard(' ');
      expect(checkedRows()).toEqual([name(1)]);
      act(() => {
        checkbox(3).focus();
      });
      await user().keyboard('{Shift>} {/Shift}');
      expect(checkedRows()).toEqual([name(1), name(2), name(3)]);
      await user().keyboard(' ');
      expect(checkedRows()).toEqual([name(1), name(2)]);
    });

    it('selects a range with Shift and a click', async () => {
      server.use(onePage(...[1, 2, 3, 4, 5].map((n) => removed(n))));
      await openRemoved();
      await startSelecting();

      const typing = user();
      await typing.click(checkbox(4));
      await typing.keyboard('{Shift>}');
      await typing.click(checkbox(2));
      await typing.keyboard('{/Shift}');

      expect(checkedRows()).toEqual([name(2), name(3), name(4)]);
    });

    it('selects a lone row with Shift when nothing was toggled yet', async () => {
      server.use(onePage(removed(1), removed(2), removed(3)));
      await openRemoved();
      await startSelecting();
      act(() => {
        checkbox(2).focus();
      });

      await user().keyboard('{Shift>} {/Shift}');

      expect(checkedRows()).toEqual([name(2)]);
    });

    it.each(['Control', 'Meta'])('selects every loaded row with %s+A', async (modifier) => {
      server.use(onePage(removed(1), removed(2), removed(3)));
      await openRemoved();
      await startSelecting();
      act(() => {
        checkbox(1).focus();
      });

      await user().keyboard(`{${modifier}>}a{/${modifier}}`);

      expect(checkedRows()).toEqual([name(1), name(2), name(3)]);
    });

    it('keeps Ctrl+A from selecting the page text in select mode only', async () => {
      server.use(onePage(removed(1), removed(2)));
      await openRemoved();
      expect(fireEvent.keyDown(row(1), { key: 'a', ctrlKey: true })).toBe(true);

      await startSelecting();

      expect(fireEvent.keyDown(checkbox(1), { key: 'a', ctrlKey: true })).toBe(false);
    });

    it('leaves Ctrl+A and a plain A alone outside select mode', async () => {
      server.use(onePage(removed(1), removed(2)));
      await openRemoved();
      act(() => {
        row(1).focus();
      });

      await user().keyboard('a{Control>}a{/Control}');

      expect(screen.queryByRole('checkbox')).toBeNull();
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('selects every row on Ctrl+A or Cmd+A when all are loaded', async () => {
      server.use(onePage(removed(1), removed(2)));
      await openRemoved();
      await startSelecting();

      fireEvent.keyDown(checkbox(1), { key: 'a', ctrlKey: true });
      expect(checkedRows()).toEqual([name(1), name(2)]);

      await user().click(within(topBar()).getByRole('button', { name: 'Deselect all' }));
      fireEvent.keyDown(checkbox(1), { key: 'a', metaKey: true });
      expect(checkedRows()).toEqual([name(1), name(2)]);
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('selects nothing on a plain A', async () => {
      server.use(onePage(removed(1), removed(2)));
      await openRemoved();
      await startSelecting();
      act(() => {
        checkbox(1).focus();
      });

      await user().keyboard('a{Control>}b{/Control}');

      expect(checkedRows()).toEqual([]);
    });
  });
});

describe('forgetting', () => {
  it('asks first, naming the count, saying what is forgotten and that files on the NAS stay, with Cancel focused', async () => {
    const sent: string[][] = [];
    server.use(onePage(removed(1), removed(2)), purgeAnswers(sent));
    const { container } = await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await user().click(checkbox(2));

    await user().click(selectionBar() as HTMLElement);

    expect(
      within(confirmation()).getByRole('heading', { name: 'Forget 2 memories?' }),
    ).toBeVisible();
    expect(
      within(confirmation()).getByText(
        'Anichron forgets them for good, with their stars, likes and hidden state. Files on your NAS are never touched; a file that turns up again comes back as a new memory.',
      ),
    ).toBeVisible();
    expect(within(confirmation()).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    expect(await axeResults(container.ownerDocument.body)).toHaveNoViolations();

    await user().keyboard('{Enter}');
    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(sent).toEqual([]);
    expect(checkedRows()).toEqual([name(1), name(2)]);
  });

  it('says it and its for a single memory, and closes on Esc without forgetting', async () => {
    const sent: string[][] = [];
    server.use(onePage(removed(1)), purgeAnswers(sent));
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await user().click(selectionBar() as HTMLElement);

    expect(within(confirmation()).getByRole('heading', { name: 'Forget 1 memory?' })).toBeVisible();
    expect(
      within(confirmation()).getByText(
        'Anichron forgets it for good, with its stars, likes and hidden state. The file on your NAS is never touched; if it turns up again, it comes back as a new memory.',
      ),
    ).toBeVisible();
    await user().keyboard('{Escape}');

    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(sent).toEqual([]);
  });

  it('does not close when the backdrop is tapped', async () => {
    server.use(onePage(removed(1)));
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await user().click(selectionBar() as HTMLElement);

    const backdrop = confirmation().closest('[data-rac]')?.parentElement as HTMLElement;
    await user().click(backdrop);

    expect(confirmation()).toBeVisible();
  });

  it('sends the selection and takes it out at once, with a toast counting what was selected, leaving select mode', async () => {
    const sent: string[][] = [];
    const answer = hold();
    server.use(
      pages([removed(1), removed(2), removed(3)]),
      purgeAnswers(sent, async (ids) => {
        await answer.until;
        return ids;
      }),
    );
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await user().click(checkbox(3));

    await forgetSelected();
    await advance(0);
    expect(sent).toEqual([[id(1), id(3)]]);
    answer.release();

    await toast('Forgot 2 memories');
    await advance(1000);
    expect(asked).toEqual([null]);
    expect(screen.queryByText(/Couldn't forget/)).toBeNull();
    expect(screen.queryByText(name(1))).toBeNull();
    expect(screen.queryByText(name(3))).toBeNull();
    expect(row(2)).toBeVisible();
    expect(screen.queryByRole('checkbox')).toBeNull();
  });

  it('counts a Live Photo once, though its video is accepted with it', async () => {
    const sent: string[][] = [];
    server.use(
      onePage(removed(1, { mediaType: 'LivePhoto', livePhotoVideoUrl: proxyPath(id(9), 'video') })),
      purgeAnswers(sent, (ids) => [...ids, id(9)]),
    );
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));

    await forgetSelected();

    expect(await toast('Forgot 1 memory')).toBeVisible();
  });

  it('takes out the ones not accepted too, loads the list again and says they may be back in the stories', async () => {
    const sent: string[][] = [];
    let items = [removed(1), removed(2), removed(3)];
    server.use(
      softDeletedAnswers(() => {
        asked.push(null);
        return { items, next: null };
      }),
      purgeAnswers(sent, () => [id(1)]),
    );
    await openRemoved();
    await startSelecting();
    await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
    items = [removed(1), removed(2), removed(3)];

    await forgetSelected();

    expect(
      await toast("Forgot 1 memory. 2 couldn't be forgotten; they may be back in your stories."),
    ).toBeVisible();
    await waitFor(() => {
      expect(asked).toHaveLength(2);
    });
    expect(screen.queryByText(name(1))).toBeNull();
    expect(screen.queryByText(name(2))).toBeNull();
    expect(screen.queryByText(name(3))).toBeNull();
  });

  it('says it may be back when a single one is not accepted', async () => {
    server.use(
      onePage(removed(1), removed(2)),
      purgeAnswers([], () => [id(1)]),
    );
    await openRemoved();
    await startSelecting();
    await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));

    await forgetSelected();

    expect(
      await toast("Forgot 1 memory. 1 couldn't be forgotten; it may be back in your stories."),
    ).toBeVisible();
  });

  it('says only that none could be forgotten when none is accepted', async () => {
    server.use(
      pages([removed(1), removed(2)]),
      purgeAnswers([], () => []),
    );
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));

    await forgetSelected();

    expect(await toast("1 couldn't be forgotten; it may be back in your stories.")).toBeVisible();
    expect(screen.queryByText(/Forgot/)).toBeNull();
    await waitFor(() => {
      expect(asked).toEqual([null, null]);
    });
  });

  it('keeps a forgotten memory out of the list for the session, though the list still has it', async () => {
    server.use(onePage(removed(1), removed(2)), purgeAnswers([]));
    const { router } = await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await forgetSelected();
    await toast('Forgot 1 memory');

    await advance(miraSession.expiresIn * 1000);
    await act(() => router.navigate({ to: '/settings' }));
    await act(() => router.navigate({ to: '/removed' }));

    expect(await screen.findByText(name(2))).toBeVisible();
    expect(screen.queryByText(name(1))).toBeNull();
  });

  it('lists a forgotten memory again once signed out and in', async () => {
    server.use(
      onePage(removed(1), removed(2)),
      purgeAnswers([]),
      logoutSucceeds(),
      loginRespondsWith(() => HttpResponse.json(miraSession)),
    );
    const { router } = await openRemoved();
    await startSelecting();
    await user().click(checkbox(1));
    await forgetSelected();
    await toast('Forgot 1 memory');

    await act(() => router.navigate({ to: '/settings' }));
    await user().click(await screen.findByRole('button', { name: 'Sign out' }));
    expect(await screen.findByRole('heading', { name: 'Sign in to Anichron' })).toBeVisible();
    signIn('mira');
    await screen.findByRole('heading', { level: 1, name: 'On This Day' });
    await act(() => router.navigate({ to: '/removed' }));

    expect(await screen.findByText(name(1))).toBeVisible();
  });

  it('sends more than a thousand in chunks of a thousand, one after another', async () => {
    const sent: string[][] = [];
    const all = Array.from({ length: 1001 }, (_, index) => removed(index + 1));
    server.use(onePage(...all), purgeAnswers(sent));
    await openRemoved();
    await startSelecting();
    await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));
    expect(selectionBar()).toHaveTextContent('Forget 1001');

    await forgetSelected();

    expect(await toast('Forgot 1001 memories')).toBeVisible();
    expect(sent.map((chunk) => chunk.length)).toEqual([1000, 1]);
    expect(sent.flat()).toEqual(all.map((item) => item.id));
    expect(screen.getByText('Nothing removed')).toBeVisible();
  }, 30_000);

  it('stops at a failed chunk, keeping what was not sent listed and selected', async () => {
    const sent: string[][] = [];
    const all = Array.from({ length: 1002 }, (_, index) => removed(index + 1));
    server.use(
      onePage(...all),
      purgeAnswers(sent, (ids) => (sent.length === 1 ? ids : fails())),
    );
    await openRemoved();
    await startSelecting();
    await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));

    await forgetSelected();

    expect(await toast("Couldn't forget 2 memories. Try again.")).toBeVisible();
    expect(await toast('Forgot 1000 memories')).toBeVisible();
    expect(sent.map((chunk) => chunk.length)).toEqual([1000, 2]);
    expect(checkedRows()).toEqual([name(1001), name(1002)]);
    expect(selectionBar()).toHaveTextContent('Forget 2');
  }, 30_000);

  it('keeps everything listed and selected when the first chunk fails', async () => {
    server.use(
      onePage(removed(1), removed(2)),
      purgeAnswers([], () => fails()),
    );
    await openRemoved();
    await startSelecting();
    await user().click(checkbox(2));

    await forgetSelected();

    expect(await toast("Couldn't forget 1 memory. Try again.")).toBeVisible();
    expect(screen.queryByText(/^Forgot/)).toBeNull();
    expect(checkedRows()).toEqual([name(2)]);
  });
});

describe('dragging', () => {
  const rowHeight = 60;
  const scrollBy = window.scrollBy.bind(window);
  let scrolled: number[];

  const at = (y: number) => ({
    clientX: 20,
    clientY: y,
    pointerId: 1,
    isPrimary: true,
    button: 0,
    pointerType: 'mouse',
  });
  const onRow = (n: number) => at((n - 1) * rowHeight + 30);
  const tick = (n: number) => checkbox(n).firstElementChild as HTMLElement;

  function dragFrom(n: number, from: HTMLElement = tick(n)) {
    fireEvent.pointerDown(from, onRow(n));
    return {
      to: (m: number) => {
        fireEvent.pointerMove(document.body, onRow(m));
      },
      toY: (y: number) => {
        fireEvent.pointerMove(document.body, at(y));
      },
      release: (m = n) => {
        fireEvent.pointerUp(document.body, onRow(m));
      },
    };
  }

  function scrollY(y: number) {
    Object.defineProperty(window, 'scrollY', { value: y, configurable: true });
  }

  beforeEach(() => {
    scrolled = [];
    scrollY(0);
    Object.defineProperty(document, 'elementFromPoint', {
      configurable: true,
      value: (_x: number, y: number) =>
        document.querySelector(
          `[data-index="${String(Math.floor((y + window.scrollY) / rowHeight))}"]`,
        ),
    });
    window.scrollBy = ((_x: number, y: number) => {
      scrolled.push(y);
      scrollY(window.scrollY + y);
      fireEvent.scroll(window);
    }) as typeof window.scrollBy;
  });

  afterEach(() => {
    Reflect.deleteProperty(document, 'elementFromPoint');
    window.scrollBy = scrollBy;
    scrollY(0);
  });

  async function selectingFive() {
    server.use(onePage(...[1, 2, 3, 4, 5].map((n) => removed(n))));
    await openRemoved();
    await startSelecting();
  }

  it('selects every row the drag crosses from a checkbox, announcing the count', async () => {
    await selectingFive();

    const drag = dragFrom(2);
    drag.to(3);
    drag.to(4);
    drag.release(4);

    expect(checkedRows()).toEqual([name(2), name(3), name(4)]);
    expect(screen.getByText('3 memories selected')).toBeVisible();
  });

  it('deselects instead when the drag starts on a selected row', async () => {
    await selectingFive();
    await user().click(within(topBar()).getByRole('button', { name: 'Select all' }));

    const drag = dragFrom(2);
    drag.to(3);
    drag.release(3);

    expect(checkedRows()).toEqual([name(1), name(4), name(5)]);
  });

  it('shrinks the range as the drag moves back, and keeps the first row when it ends there', async () => {
    await selectingFive();

    const drag = dragFrom(2);
    drag.to(5);
    drag.to(3);
    expect(checkedRows()).toEqual([name(2), name(3)]);
    drag.to(2);
    drag.release(2);

    expect(checkedRows()).toEqual([name(2)]);
  });

  it('paints upwards too, keeping what was selected before the drag', async () => {
    await selectingFive();
    await user().click(checkbox(5));

    const drag = dragFrom(3);
    drag.to(1);
    drag.release(1);

    expect(checkedRows()).toEqual([name(1), name(2), name(3), name(5)]);
  });

  it('ignores a pointer over no row', async () => {
    await selectingFive();

    const drag = dragFrom(2);
    drag.to(3);
    drag.toY(10 * rowHeight);
    drag.release(3);

    expect(checkedRows()).toEqual([name(2), name(3)]);
  });

  it('selects nothing when the drag starts outside the checkboxes', async () => {
    await selectingFive();

    const drag = dragFrom(2, within(checkbox(2)).getByText(name(2)));
    drag.to(4);
    drag.release(4);

    expect(checkedRows()).toEqual([]);
  });

  it('takes no drag from a button other than the primary one', async () => {
    await selectingFive();

    fireEvent.pointerDown(tick(2), { ...onRow(2), button: 2 });
    fireEvent.pointerMove(document.body, onRow(4));
    fireEvent.pointerUp(document.body, onRow(4));

    expect(checkedRows()).toEqual([]);
  });

  const touchMoveHeld = () => {
    const touch = new Event('touchmove', { cancelable: true });
    window.dispatchEvent(touch);
    return touch.defaultPrevented;
  };
  const overTarget = { ...at(0), clientX: 0 };

  it('holds touch scrolling only while dragging', async () => {
    await selectingFive();
    expect(touchMoveHeld()).toBe(false);

    const drag = dragFrom(2);
    expect(touchMoveHeld()).toBe(true);
    drag.release();

    expect(touchMoveHeld()).toBe(false);
  });

  it('lets go of touch scrolling when the screen is left mid-drag', async () => {
    server.use(onePage(removed(1), removed(2)));
    const { router } = await openRemoved();
    await startSelecting();
    dragFrom(2);

    await act(() => router.navigate({ to: '/settings' }));

    expect(touchMoveHeld()).toBe(false);
  });

  it('keeps the range it ended on when released over its own checkbox', async () => {
    await selectingFive();

    const drag = dragFrom(2);
    drag.to(3);
    drag.to(2);
    fireEvent.pointerUp(tick(2), overTarget);
    fireEvent.click(tick(2), { ...overTarget, detail: 1 });

    expect(checkedRows()).toEqual([name(2)]);
  });

  it('ranges with Shift from where the last drag started', async () => {
    await selectingFive();
    const drag = dragFrom(2);
    drag.to(3);
    drag.release(3);

    const typing = user();
    await typing.keyboard('{Shift>}');
    await typing.click(checkbox(5));
    await typing.keyboard('{/Shift}');

    expect(checkedRows()).toEqual([name(2), name(3), name(4), name(5)]);
  });

  it('starts over when a new drag begins before the last one was released', async () => {
    await selectingFive();

    dragFrom(1);
    const drag = dragFrom(4);
    drag.to(5);
    drag.release(5);
    fireEvent.pointerMove(document.body, onRow(2));

    expect(checkedRows()).toEqual([name(4), name(5)]);
  });

  it('stops following once released or cancelled', async () => {
    await selectingFive();

    const first = dragFrom(1);
    first.to(2);
    first.release(2);
    fireEvent.pointerMove(document.body, onRow(4));
    expect(checkedRows()).toEqual([name(1), name(2)]);

    const second = dragFrom(5);
    second.to(4);
    fireEvent.pointerCancel(document.body, onRow(4));
    fireEvent.pointerMove(document.body, onRow(3));

    expect(checkedRows()).toEqual([name(1), name(2), name(4), name(5)]);
  });

  it('scrolls by itself near the bottom edge, loading and selecting the next page, until released', async () => {
    pageHeight(1600);
    server.use(
      pages(
        Array.from({ length: 10 }, (_, index) => removed(index + 1)),
        Array.from({ length: 10 }, (_, index) => removed(index + 11)),
      ),
    );
    await openRemoved();
    await startSelecting();

    const drag = dragFrom(1);
    drag.toY(innerHeight - 70);
    await advance(160);

    expect(asked).toEqual([null, 'page1']);
    expect(scrolled.every((step) => step === 12)).toBe(true);
    expect(checkbox(14)).toBeChecked();
    expect(checkedRows()).toEqual(Array.from({ length: 14 }, (_, index) => name(index + 1)));
    drag.release(14);
    const steps = scrolled.length;
    await advance(1000);
    expect(scrolled).toHaveLength(steps);
  });

  it('scrolls up near the top edge, and stops once the pointer leaves the edge', async () => {
    await selectingFive();
    scrollY(120);

    const drag = dragFrom(4);
    drag.toY(70);
    await advance(48);
    expect(scrolled).toEqual([-12, -12, -12]);
    drag.toY(200);
    await advance(1000);
    drag.release();

    expect(scrolled).toEqual([-12, -12, -12]);
  });

  it('jumps instead of gliding when motion is reduced', async () => {
    vi.spyOn(window, 'matchMedia').mockImplementation(
      (media) =>
        ({
          matches: media === '(prefers-reduced-motion: reduce)',
          media,
          addEventListener: () => undefined,
          removeEventListener: () => undefined,
        }) as unknown as MediaQueryList,
    );
    await selectingFive();

    const drag = dragFrom(1);
    drag.toY(innerHeight - 10);
    await advance(399);
    expect(scrolled).toEqual([]);
    await advance(1);
    drag.release();

    expect(scrolled).toEqual([innerHeight / 2]);
  });

  describe('from a long press', () => {
    it('turns select mode on with the row selected and carries on into a drag, without opening it', async () => {
      server.use(onePage(...[1, 2, 3, 4].map((n) => removed(n))));
      await openRemoved();

      fireEvent.pointerDown(row(2), onRow(2));
      await advance(500);
      expect(checkedRows()).toEqual([name(2)]);
      fireEvent.pointerMove(document.body, onRow(3));
      fireEvent.pointerMove(document.body, onRow(4));
      fireEvent.pointerUp(document.body, onRow(4));
      await advance(100);

      expect(checkedRows()).toEqual([name(2), name(3), name(4)]);
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('selects only the held row when released where it started', async () => {
      server.use(onePage(removed(1), removed(2)));
      await openRemoved();

      fireEvent.pointerDown(row(1), overTarget);
      await advance(500);
      fireEvent.pointerUp(checkbox(1), overTarget);
      fireEvent.click(checkbox(1), { ...overTarget, detail: 1 });
      await advance(100);

      expect(checkedRows()).toEqual([name(1)]);
      expect(screen.queryByRole('dialog')).toBeNull();
      await user().click(checkbox(2));
      expect(checkedRows()).toEqual([name(1), name(2)]);
    });

    it('leaves a shorter press opening the row', async () => {
      server.use(onePage(removed(1)));
      await openRemoved();

      fireEvent.pointerDown(row(1), onRow(1));
      await advance(400);
      fireEvent.pointerUp(row(1), onRow(1));
      await advance(100);

      expect(screen.queryByRole('checkbox')).toBeNull();
      expect(viewer()).toBeVisible();
    });
  });
});

describe('forgetting from the viewer', () => {
  async function openViewer(n: number) {
    await user().click(row(n));
    return viewer();
  }

  async function forgetShown() {
    await user().click(within(viewer()).getByRole('button', { name: 'Forget' }));
    await user().click(within(confirmation()).getByRole('button', { name: 'Forget' }));
  }

  it('asks first, then forgets the memory on screen and moves to the next', async () => {
    const sent: string[][] = [];
    server.use(onePage(removed(1), removed(2), removed(3)), purgeAnswers(sent));
    const { container } = await openRemoved();
    await openViewer(2);

    await user().click(within(viewer()).getByRole('button', { name: 'Forget' }));
    expect(within(confirmation()).getByRole('heading', { name: 'Forget 1 memory?' })).toBeVisible();
    expect(within(confirmation()).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    expect(await axeResults(container.ownerDocument.body)).toHaveNoViolations();
    await user().click(within(confirmation()).getByRole('button', { name: 'Forget' }));

    expect(await toast('Forgot 1 memory')).toBeInTheDocument();
    expect(sent).toEqual([[id(2)]]);
    expect(caption()).toHaveTextContent(name(3));
  });

  it('keeps the memory when the confirmation is cancelled', async () => {
    const sent: string[][] = [];
    server.use(onePage(removed(1), removed(2)), purgeAnswers(sent));
    await openRemoved();
    await openViewer(1);

    await user().click(within(viewer()).getByRole('button', { name: 'Forget' }));
    await user().click(within(confirmation()).getByRole('button', { name: 'Cancel' }));

    expect(sent).toEqual([]);
    expect(caption()).toHaveTextContent(name(1));
  });

  it('closes once the last memory is forgotten', async () => {
    server.use(onePage(removed(1)), purgeAnswers([]));
    await openRemoved();
    await openViewer(1);

    await forgetShown();

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).toBeNull();
    });
    expect(screen.getByText('Nothing removed')).toBeVisible();
  });
});
