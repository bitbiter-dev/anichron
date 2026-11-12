import { fireEvent, screen, within } from '@testing-library/react';
import { delay, HttpResponse } from 'msw';
import { act } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { components } from '../../api/generated/v1';
import { axeResults } from '../../test/axe';
import {
  adminSession,
  miraSession,
  problemResponse,
  refreshRespondsWith,
  refreshSucceeds,
} from '../../test/handlers/auth';
import {
  cancelAnswers,
  itemId,
  workItem,
  workItemAnswers,
  workItemEvents,
  workItemPages,
  type Connection,
  type Listed,
} from '../../test/handlers/work-items';
import { memory, onThisDayAnswers, yearGroup } from '../../test/handlers/flashbacks';
import { renderApp } from '../../test/render-app';
import { server } from '../../test/server';
import { sharedTabs } from '../../test/tabs';
import { becomes } from '../../test/visibility';

type WorkItem = components['schemas']['WorkItemDto'];
type Page = Partial<components['schemas']['WorkItemPageDto']>;

const listCursor = '2026-11-12T12:02:00.123456Z';

let listed: Listed[];
let events: ReturnType<typeof workItemEvents>;

async function advance(milliseconds: number) {
  await act(() => vi.advanceTimersByTimeAsync(milliseconds));
}

async function opened(count: number): Promise<Connection> {
  for (let step = 0; events.connections.length < count; step++) {
    if (step === 100) throw new Error(`stream ${String(count)} never opened.`);
    await advance(0);
  }
  for (let step = 0; step < 10; step++) await advance(0);
  return events.connections[count - 1] as Connection;
}

const lastEventIds = () => events.connections.map(({ lastEventId }) => lastEventId);

async function on(connection: Connection, id: string, item: WorkItem) {
  act(() => {
    connection.send(id, item);
  });
  for (let step = 0; step < 5; step++) await advance(0);
}

const rows = () => within(screen.getByRole('list')).getAllByRole('listitem');
const kinds = () => rows().map((row) => within(row).getAllByRole('link')[0]?.textContent);
const rowOf = (kind: string) =>
  rows().find((row) => within(row).queryByRole('link', { name: kind })) as HTMLElement;
const reconnecting = () => screen.getByRole('status');

function lists(pages: Page | ((page: number) => Page | Response | Promise<Page>)) {
  server.use(workItemPages(typeof pages === 'function' ? pages : () => pages, listed));
}

async function openList(pages: Page, path = '/admin/work-items') {
  lists(pages);
  const rendered = renderApp(path);
  await screen.findByRole('list');
  return { ...rendered, stream: await opened(1) };
}

beforeEach(() => {
  listed = [];
  events = workItemEvents();
  vi.setSystemTime(new Date('2026-11-12T12:30:00Z'));
  server.use(refreshSucceeds(adminSession), events.handler);
});

describe('the work queue', () => {
  it('lists the newest requests, then follows the stream from the list cursor', async () => {
    const { container } = await openList({
      items: [
        workItem(2, {
          kind: 'ProxySweep',
          status: 'Running',
          progress: { done: 3, total: 10, note: 'shard 4' },
        }),
        workItem(1, { status: 'Succeeded' }),
      ],
      cursor: listCursor,
    });

    expect(kinds()).toEqual(['Proxy sweep', 'Crawl']);
    expect(within(rowOf('Proxy sweep')).getByText('Running')).toBeVisible();
    expect(within(rowOf('Proxy sweep')).getByText('3 of 10 shard 4')).toBeVisible();
    expect(within(rowOf('Proxy sweep')).getByRole('progressbar')).toHaveAttribute('value', '3');
    expect(
      within(rowOf('Proxy sweep')).getByText(
        /^Requested \w{3} \d{1,2}, 2026, \d{1,2}:\d{2} [AP]M$/,
      ),
    ).toBeVisible();
    expect(within(rowOf('Crawl')).getByText('Succeeded')).toBeVisible();
    expect(lastEventIds()).toEqual([listCursor]);
    expect(events.connections[0]?.bearer).toBe(`Bearer ${adminSession.accessToken}`);
    expect(listed).toEqual([{ page: 1, bearer: `Bearer ${adminSession.accessToken}` }]);
    expect(reconnecting()).toBeEmptyDOMElement();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('shows an item with no total as under way, with its note', async () => {
    await openList({
      items: [
        workItem(1, { status: 'Running', progress: { done: 7, total: null, note: 'walking' } }),
      ],
    });

    expect(within(rowOf('Crawl')).getByRole('progressbar')).not.toHaveAttribute('value');
    expect(within(rowOf('Crawl')).getByText('walking')).toBeVisible();
    expect(within(rowOf('Crawl')).queryByText(/ of /)).toBeNull();
  });

  it('labels every kind', async () => {
    const all = [
      'TokenCleanup',
      'WorkItemRetention',
      'Crawl',
      'BurstDetection',
      'ProxySweep',
      'ProxyDeletion',
    ] as const;
    await openList({ items: all.map((kind, n) => workItem(n + 1, { kind })) });

    expect(kinds()).toEqual([
      'Token cleanup',
      'Work item retention',
      'Crawl',
      'Burst detection',
      'Proxy sweep',
      'Proxy deletion',
    ]);
  });

  it('says so when the queue is empty', async () => {
    lists({ items: [] });
    renderApp('/admin/work-items');

    expect(await screen.findByText('Nothing in the queue.')).toBeVisible();
  });

  it('says it cannot reach the server, and lists again on retry', async () => {
    let answers = 0;
    lists(() =>
      ++answers <= 4 ? problemResponse(503, 'service_unavailable') : { items: [workItem(1)] },
    );
    renderApp('/admin/work-items');

    expect(
      await screen.findByText(
        "Can't reach Anichron. Check your connection.",
        {},
        { timeout: 60_000 },
      ),
    ).toBeVisible();
    await advance(10_000);
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('link', { name: 'Crawl' })).toBeVisible();
  });

  it('says the list failed for any other refusal', async () => {
    lists(() => HttpResponse.json({ status: 500 }, { status: 500 }));
    renderApp('/admin/work-items');

    expect(
      await screen.findByText("Couldn't load the work queue.", {}, { timeout: 60_000 }),
    ).toBeVisible();
  });

  it('pages through older requests', async () => {
    lists((page) => ({
      items: page === 1 ? [workItem(4), workItem(3)] : [workItem(2, { kind: 'ProxySweep' })],
      page,
      pageSize: 2,
      total: 3,
    }));
    renderApp('/admin/work-items');
    await screen.findByRole('list');
    const pages = () => screen.getByRole('navigation', { name: 'Pages' });

    expect(within(pages()).queryByRole('link', { name: 'Newer' })).toBeNull();
    fireEvent.click(within(pages()).getByRole('link', { name: 'Older' }));

    expect(await screen.findByRole('link', { name: 'Proxy sweep' })).toBeVisible();
    expect(within(pages()).queryByRole('link', { name: 'Older' })).toBeNull();
    fireEvent.click(within(pages()).getByRole('link', { name: 'Newer' }));
    expect(await screen.findAllByRole('link', { name: 'Crawl' })).toHaveLength(2);
    expect(listed.map(({ page }) => page)).toEqual([1, 2]);

    await on(
      await opened(1),
      'a',
      workItem(4, { status: 'Running', updatedAt: '2026-11-12T12:10:00Z' }),
    );
    expect(await within(rows()[0] as HTMLElement).findByText('Running')).toBeVisible();
    expect(rows()).toHaveLength(2);
    fireEvent.click(within(pages()).getByRole('link', { name: 'Older' }));
    expect(await screen.findByRole('link', { name: 'Proxy sweep' })).toBeVisible();
    await advance(0);
    expect(listed.map(({ page }) => page)).toEqual([1, 2, 2]);
  });

  it('keeps the newer copy of an item another page already shows', async () => {
    lists((page) => ({
      items: [
        page === 1
          ? workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:10:00Z' })
          : workItem(1),
      ],
      page,
      pageSize: 1,
      total: 2,
    }));
    renderApp('/admin/work-items');
    await screen.findByText('Running');

    fireEvent.click(screen.getByRole('link', { name: 'Older' }));
    await screen.findByRole('link', { name: 'Newer' });
    await advance(0);

    expect(listed.map(({ page }) => page)).toEqual([1, 2]);
    expect(screen.getByText('Running')).toBeVisible();
  });

  it('opens the page the address names', async () => {
    lists((page) => ({ items: [workItem(page)], page, pageSize: 1, total: 5 }));
    renderApp('/admin/work-items?page=3');
    await screen.findByRole('list');
    await opened(1);

    expect(listed.map(({ page }) => page).toSorted()).toEqual([1, 3]);
  });

  it('opens the first page for an address with no page number', async () => {
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items?page=soon');
    await screen.findByRole('list');

    expect(listed.map(({ page }) => page)).toEqual([1]);
  });
});

describe('an event', () => {
  it('moves its row along', async () => {
    const { stream } = await openList({
      items: [workItem(1), workItem(0, { kind: 'ProxySweep' })],
    });

    await on(
      stream,
      '2026-11-12T12:05:00Z',
      workItem(1, {
        status: 'Running',
        updatedAt: '2026-11-12T12:05:00Z',
        progress: { done: 4, total: 9, note: null },
      }),
    );

    expect(await within(rowOf('Crawl')).findByText('Running')).toBeVisible();
    expect(within(rowOf('Crawl')).getByText('4 of 9')).toBeVisible();
    expect(kinds()).toEqual(['Crawl', 'Proxy sweep']);
  });

  it('changes nothing when it is older than what is shown', async () => {
    const { stream } = await openList({
      items: [workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:05:00.5Z' })],
    });

    await on(stream, 'a', workItem(1, { status: 'Queued', updatedAt: '2026-11-12T12:05:00.123Z' }));
    await on(stream, 'b', workItem(1, { status: 'Queued', updatedAt: '2026-11-12T12:05:00Z' }));
    await on(
      stream,
      'c',
      workItem(1, { status: 'Queued', updatedAt: '2026-11-12T12:04:59.9999999Z' }),
    );
    await advance(0);

    expect(within(rowOf('Crawl')).getByText('Running')).toBeVisible();
  });

  it('wins over what is shown when it was updated in the same instant', async () => {
    const { stream } = await openList({
      items: [workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:05:00.5Z' })],
    });

    await on(
      stream,
      'a',
      workItem(1, { status: 'Succeeded', updatedAt: '2026-11-12T12:05:00.5Z' }),
    );

    expect(await within(rowOf('Crawl')).findByText('Succeeded')).toBeVisible();
  });

  it('is ordered by the microsecond, not the millisecond', async () => {
    const { stream } = await openList({ items: [workItem(1)] });

    await on(
      stream,
      'a',
      workItem(1, { status: 'Succeeded', updatedAt: '2026-11-12T12:05:00.123457Z' }),
    );
    await on(
      stream,
      'b',
      workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:05:00.123456Z' }),
    );
    await advance(0);

    expect(within(rowOf('Crawl')).getByText('Succeeded')).toBeVisible();
  });

  it('adds a newer request to the first page in its place', async () => {
    const { stream } = await openList({
      items: [
        workItem(5, { kind: 'ProxySweep' }),
        workItem(4, { kind: 'BurstDetection' }),
        workItem(2, { kind: 'Crawl' }),
      ],
      pageSize: 3,
      total: 3,
    });
    expect(screen.queryByRole('link', { name: 'Older' })).toBeNull();

    await on(stream, 'a', workItem(3, { kind: 'TokenCleanup' }));
    await on(
      stream,
      'b',
      workItem(6, { kind: 'ProxyDeletion', requestedAt: '2026-11-12T12:04:30.25Z' }),
    );

    expect(kinds()).toEqual([
      'Proxy sweep',
      'Proxy deletion',
      'Burst detection',
      'Token cleanup',
      'Crawl',
    ]);
    expect(screen.getByRole('link', { name: 'Older' })).toBeVisible();
  });

  it('adds the first request to an empty queue', async () => {
    lists({ items: [] });
    renderApp('/admin/work-items');
    await screen.findByText('Nothing in the queue.');

    await on(await opened(1), 'a', workItem(1));

    expect(await screen.findByRole('link', { name: 'Crawl' })).toBeVisible();
  });

  it('leaves a request older than the first page to its own page', async () => {
    const { stream } = await openList({
      items: [workItem(5, { kind: 'ProxySweep' }), workItem(3, { kind: 'BurstDetection' })],
    });

    await on(stream, 'a', workItem(2, { kind: 'TokenCleanup' }));
    await on(stream, 'b', workItem(3, { kind: 'Crawl', id: itemId(30) }));
    await advance(0);

    expect(kinds()).toEqual(['Proxy sweep', 'Burst detection']);
  });

  it('is not overwritten by a page that was read before it', async () => {
    let answerPageTwo: (page: Page) => void = () => undefined;
    lists((page) =>
      page === 1
        ? { items: [workItem(9)], pageSize: 1, total: 2 }
        : new Promise<Page>((resolve) => {
            answerPageTwo = resolve;
          }),
    );
    renderApp('/admin/work-items');
    await screen.findByRole('list');
    const stream = await opened(1);
    fireEvent.click(screen.getByRole('link', { name: 'Older' }));
    await advance(0);

    await on(stream, 'a', workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:20:00Z' }));
    answerPageTwo({ items: [workItem(1)], page: 2, pageSize: 1, total: 2 });

    expect(await screen.findByText('Running')).toBeVisible();
  });
});

describe('the stream', () => {
  it('reconnects a second after it ends, from the last event it saw', async () => {
    const { stream } = await openList({ items: [workItem(1)], cursor: listCursor });
    await on(stream, '2026-11-12T12:06:00Z', workItem(1, { updatedAt: '2026-11-12T12:06:00Z' }));
    act(() => {
      stream.end();
    });

    await advance(999);
    expect(events.connections).toHaveLength(1);
    expect(reconnecting()).toHaveTextContent('Reconnecting…');
    await advance(1);
    await opened(2);

    expect(lastEventIds()).toEqual([listCursor, '2026-11-12T12:06:00Z']);
    expect(reconnecting()).toBeEmptyDOMElement();
  });

  it('keeps the last event id when an event carries none', async () => {
    const { stream } = await openList({ items: [workItem(1)], cursor: listCursor });
    act(() => {
      stream.write(`event: work-item\ndata: ${JSON.stringify(workItem(1))}\n\n`);
      stream.drop();
    });
    await advance(1000);
    await opened(2);

    expect(lastEventIds()).toEqual([listCursor, listCursor]);
  });

  it('backs off, doubling up to half a minute, while the server cannot be reached', async () => {
    events = workItemEvents([
      () => problemResponse(503, 'service_unavailable'),
      () => problemResponse(503, 'service_unavailable'),
      () => problemResponse(503, 'service_unavailable'),
      () => problemResponse(503, 'service_unavailable'),
      () => problemResponse(503, 'service_unavailable'),
      () => problemResponse(503, 'service_unavailable'),
      () => problemResponse(503, 'service_unavailable'),
    ]);
    server.use(events.handler);
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items');
    await screen.findByRole('list');
    await opened(1);

    for (const [count, wait] of [
      [2, 1000],
      [3, 2000],
      [4, 4000],
      [5, 8000],
      [6, 16_000],
      [7, 30_000],
      [8, 30_000],
    ] as const) {
      await advance(wait - 1);
      expect(events.connections).toHaveLength(count - 1);
      await advance(1);
      await opened(count);
    }
    expect(reconnecting()).toBeEmptyDOMElement();
  });

  it('starts the backoff again once it connects', async () => {
    events = workItemEvents([() => problemResponse(503, 'service_unavailable')]);
    server.use(events.handler);
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items');
    await screen.findByRole('list');
    await opened(1);
    await advance(1000);
    const stream = await opened(2);
    act(() => {
      stream.write(': open\n\n');
    });
    await advance(0);

    act(() => {
      stream.drop();
    });
    await advance(999);
    expect(events.connections).toHaveLength(2);
    await advance(1);
    await opened(3);
  });

  it('reconnects a second after the network fails', async () => {
    events = workItemEvents([() => HttpResponse.error()]);
    server.use(events.handler);
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items');
    await screen.findByRole('list');
    await opened(1);

    await advance(999);
    expect(events.connections).toHaveLength(1);
    await advance(1);
    await opened(2);
  });

  it('keeps saying it is reconnecting when a stream closed mid-wait is followed by another', async () => {
    events = workItemEvents([undefined, () => problemResponse(503, 'service_unavailable')]);
    server.use(events.handler);
    const { stream } = await openList({ items: [workItem(1)] });
    act(() => {
      stream.write(': open\n\n');
      stream.drop();
    });
    await advance(500);
    expect(reconnecting()).toHaveTextContent('Reconnecting…');

    act(() => {
      becomes('hidden');
    });
    await advance(0);
    expect(reconnecting()).toBeEmptyDOMElement();
    await advance(100);
    act(() => {
      becomes('visible');
    });
    await opened(2);
    expect(reconnecting()).toHaveTextContent('Reconnecting…');
    await advance(450);

    expect(reconnecting()).toHaveTextContent('Reconnecting…');
    expect(events.connections).toHaveLength(2);
  });

  it('waits as long as an unavailable server asks', async () => {
    events = workItemEvents([
      () => problemResponse(503, 'service_unavailable', { 'Retry-After': '7' }),
    ]);
    server.use(events.handler);
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items');
    await screen.findByRole('list');
    await opened(1);

    await advance(6999);
    expect(events.connections).toHaveLength(1);
    await advance(1);
    await opened(2);
  });

  it('renews an expired access token and carries on', async () => {
    let refreshes = 0;
    server.use(
      refreshRespondsWith(() =>
        HttpResponse.json({ ...adminSession, accessToken: `token-${String(++refreshes)}` }),
      ),
    );
    events = workItemEvents([() => HttpResponse.json({ status: 401 }, { status: 401 })]);
    server.use(events.handler);
    await openList({ items: [workItem(1)] });
    await advance(50);
    await opened(2);

    expect(events.connections.map(({ bearer }) => bearer)).toEqual([
      'Bearer token-1',
      'Bearer token-2',
    ]);
    expect(reconnecting()).toBeEmptyDOMElement();
  });

  it.each([403, 404])('stops on a %i and shows the screen as not found', async (status) => {
    events = workItemEvents([() => problemResponse(status, 'gone')]);
    server.use(events.handler);
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items');

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeVisible();
    expect(screen.queryByRole('list')).toBeNull();
    await advance(60_000);
    expect(events.connections).toHaveLength(1);
  });

  it('closes while the tab is hidden, and resumes from its last event when it shows again', async () => {
    const { stream } = await openList({ items: [workItem(1)], cursor: listCursor });
    await on(stream, '2026-11-12T12:07:00Z', workItem(1, { updatedAt: '2026-11-12T12:07:00Z' }));

    act(() => {
      becomes('hidden');
    });
    await advance(0);
    expect(reconnecting()).toBeEmptyDOMElement();
    await on(stream, 'b', workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:08:00Z' }));
    await advance(60_000);

    expect(within(rowOf('Crawl')).getByText('Queued')).toBeVisible();
    expect(events.connections).toHaveLength(1);
    act(() => {
      becomes('visible');
    });
    await opened(2);
    expect(lastEventIds()).toEqual([listCursor, '2026-11-12T12:07:00Z']);
  });

  it('closes when the admin leaves, and starts from a fresh list on the way back', async () => {
    let cursor = 0;
    lists(() => ({ items: [workItem(1)], cursor: `2026-11-12T12:0${String(++cursor)}:00Z` }));
    const { router } = renderApp('/admin/work-items');
    await screen.findByRole('list');
    const stream = await opened(1);

    await act(() => router.navigate({ to: '/settings' }));
    await advance(60_000);
    expect(events.connections).toHaveLength(1);
    await act(() => router.navigate({ to: '/admin/work-items' }));
    await screen.findByRole('list');
    const again = await opened(2);

    expect(lastEventIds()).toEqual(['2026-11-12T12:01:00Z', '2026-11-12T12:02:00Z']);
    expect(listed.map(({ page }) => page)).toEqual([1, 1]);
    await on(stream, 'a', workItem(7, { kind: 'ProxySweep' }));
    await on(again, 'b', workItem(6, { kind: 'TokenCleanup' }));
    expect(await screen.findByRole('link', { name: 'Token cleanup' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Proxy sweep' })).toBeNull();
  });

  it('forgets only the work queue when the admin leaves', async () => {
    let days = 0;
    server.use(
      onThisDayAnswers(async (date) => {
        if (++days > 1) await delay(5000);
        return HttpResponse.json({
          date,
          years: [yearGroup(2023, memory('0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b', 2023))],
        });
      }),
    );
    lists({ items: [workItem(1)] });
    const { router } = renderApp('/');
    expect(await screen.findByRole('link', { name: / · 2023$/ })).toBeVisible();

    await act(() => router.navigate({ to: '/admin/work-items' }));
    await screen.findByRole('list');
    await act(() => router.navigate({ to: '/' }));

    expect(screen.getByRole('link', { name: / · 2023$/ })).toBeVisible();
  });

  it('closes when the admin is signed out in another tab', async () => {
    const tabs = sharedTabs();
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items', tabs.tab());
    await screen.findByRole('list');
    await opened(1);

    act(() => {
      tabs.tab().announce('signed-out');
    });

    expect(await screen.findByRole('heading', { name: 'Sign in to Anichron' })).toBeVisible();
    await advance(60_000);
    expect(events.connections).toHaveLength(1);
  });

  it('starts again when another tab signs in', async () => {
    const tabs = sharedTabs();
    lists({ items: [workItem(1)] });
    renderApp('/admin/work-items', tabs.tab());
    await screen.findByRole('list');
    const stream = await opened(1);

    act(() => {
      tabs.tab().announce('signed-in');
    });
    await screen.findByRole('list');
    const again = await opened(2);
    await on(stream, 'a', workItem(7, { kind: 'ProxySweep' }));
    await on(again, 'b', workItem(6, { kind: 'TokenCleanup' }));

    expect(await screen.findByRole('link', { name: 'Token cleanup' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Proxy sweep' })).toBeNull();
  });
});

describe('an item', () => {
  const running = workItem(1, {
    status: 'Running',
    attempt: 2,
    stalls: 1,
    startedAt: '2026-11-12T12:01:30Z',
    updatedAt: '2026-11-12T12:03:00Z',
    progress: { done: 40, total: 100, note: '2019/07' },
    result: { found: 100, processed: 40, failures: ['a.jpg'] },
    resultIsPartial: true,
  });

  it('opens from the list with its details, and returns without listing or connecting again', async () => {
    server.use(
      workItemAnswers((id) => (id === running.id ? running : problemResponse(404, 'missing'))),
    );
    const { container } = await openList({ items: [running] });

    fireEvent.click(screen.getByRole('link', { name: 'Crawl' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Crawl' })).toBeVisible();
    expect(screen.getByRole('heading', { level: 1, name: 'Work item' })).toBeVisible();
    const details = screen.getAllByRole('definition').map((field) => field.textContent);
    expect(details[0]).toBe('Running');
    for (const at of details.slice(1, 4))
      expect(at).toMatch(/^\w{3} \d{1,2}, 2026, \d{1,2}:\d{2} [AP]M$/);
    expect(screen.getAllByRole('term').map((term) => term.textContent)).toEqual([
      'Status',
      'Requested',
      'Started',
      'Updated',
      'Attempt',
      'Stalls',
      'found',
      'processed',
      'failures',
    ]);
    expect(details.slice(4)).toEqual(['2', '1', '100', '40', '["a.jpg"]']);
    expect(screen.getByRole('heading', { level: 3, name: 'Result so far' })).toBeVisible();
    expect(screen.getByText('40 of 100 2019/07')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('link', { name: 'Crawl' })).toBeVisible();
    await advance(0);
    expect(listed).toHaveLength(1);
    expect(events.connections).toHaveLength(1);
  });

  it('opens each item as itself', async () => {
    const items = [workItem(2, { kind: 'ProxySweep' }), workItem(1)];
    server.use(workItemAnswers((id) => items.find((item) => item.id === id) ?? workItem(9)));
    await openList({ items });

    fireEvent.click(screen.getByRole('link', { name: 'Crawl' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'Crawl' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    fireEvent.click(await screen.findByRole('link', { name: 'Proxy sweep' }));

    expect(await screen.findByRole('heading', { level: 2, name: 'Proxy sweep' })).toBeVisible();
  });

  it('shows its finished result and error', async () => {
    server.use(
      workItemAnswers(() =>
        workItem(1, {
          status: 'Failed',
          finishedAt: '2026-11-12T12:04:00Z',
          cancelRequestedAt: '2026-11-12T12:03:00Z',
          result: { note: 'stopped' },
          error: 'IOException: the share went away',
        }),
      ),
    );
    lists({ items: [] });
    renderApp(`/admin/work-items/${itemId(1)}`);

    expect(await screen.findByRole('heading', { level: 3, name: 'Result' })).toBeVisible();
    expect(screen.getByText('stopped')).toBeVisible();
    expect(screen.getByRole('heading', { level: 3, name: 'Error' })).toBeVisible();
    expect(screen.getByText('IOException: the share went away')).toBeVisible();
    expect(screen.getAllByRole('definition')[0]).toHaveTextContent('Failed');
    expect(
      screen
        .getAllByRole('term')
        .map((term) => term.textContent)
        .slice(0, 5),
    ).toEqual(['Status', 'Requested', 'Finished', 'Cancel requested', 'Updated']);
    expect(screen.queryByRole('button', { name: /^Cancel/ })).toBeNull();
  });

  it('opened by its address, still follows the stream from the list cursor', async () => {
    server.use(workItemAnswers(() => workItem(1)));
    lists({ items: [], cursor: listCursor });
    renderApp(`/admin/work-items/${itemId(1)}`);
    await screen.findByRole('heading', { level: 2, name: 'Crawl' });
    const stream = await opened(1);
    expect(screen.queryByRole('heading', { level: 3 })).toBeNull();

    await on(stream, 'a', workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:00:30Z' }));
    expect(screen.getAllByRole('definition')[0]).toHaveTextContent('Queued');
    await on(stream, 'b', workItem(1, { status: 'Succeeded', updatedAt: '2026-11-12T12:09:00Z' }));

    expect(await screen.findByText('Succeeded')).toBeVisible();
    expect(lastEventIds()).toEqual([listCursor]);
  });

  it('is not overwritten by a read that started before an event', async () => {
    let answer: (item: WorkItem) => void = () => undefined;
    server.use(
      workItemAnswers(
        () =>
          new Promise<WorkItem>((resolve) => {
            answer = resolve;
          }),
      ),
    );
    const { stream } = await openList({ items: [workItem(1)] });
    fireEvent.click(screen.getByRole('link', { name: 'Crawl' }));
    await advance(0);

    await on(stream, 'a', workItem(1, { status: 'Running', updatedAt: '2026-11-12T12:20:00Z' }));
    answer(workItem(1));

    expect(await screen.findByText('Running')).toBeVisible();
  });

  it('that does not exist says so', async () => {
    server.use(workItemAnswers(() => problemResponse(404, 'missing')));
    lists({ items: [] });
    renderApp(`/admin/work-items/${itemId(9)}`);

    expect(await screen.findByText('There is no such work item.')).toBeVisible();
  });

  it('that cannot be read offers to try again', async () => {
    let reads = 0;
    server.use(
      workItemAnswers(() =>
        ++reads <= 4 ? HttpResponse.json({ status: 500 }, { status: 500 }) : workItem(1),
      ),
    );
    lists({ items: [] });
    renderApp(`/admin/work-items/${itemId(1)}`);

    expect(
      await screen.findByText("Couldn't load the work queue.", {}, { timeout: 60_000 }),
    ).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'Crawl' })).toBeVisible();
  });
});

describe('cancelling', () => {
  const cancelIn = (kind: string) =>
    within(rowOf(kind)).getByRole('button', { name: new RegExp(`^Cancel ${kind}, requested`) });

  it('is offered on queued and running items only', async () => {
    await openList({
      items: [
        workItem(5, { kind: 'ProxySweep', status: 'Queued' }),
        workItem(4, { kind: 'Crawl', status: 'Running' }),
        workItem(3, { kind: 'TokenCleanup', status: 'Succeeded' }),
        workItem(2, { kind: 'BurstDetection', status: 'Failed' }),
        workItem(1, { kind: 'ProxyDeletion', status: 'Cancelled' }),
      ],
    });

    expect(rows().map((row) => within(row).queryAllByRole('button').length)).toEqual([
      1, 1, 0, 0, 0,
    ]);
  });

  it('shows a running item as cancelling until the stream reports it cancelled', async () => {
    const item = workItem(1, { status: 'Running' });
    server.use(
      cancelAnswers(() =>
        HttpResponse.json(
          { ...item, cancelRequestedAt: '2026-11-12T12:10:00Z', updatedAt: '2026-11-12T12:10:00Z' },
          { status: 202 },
        ),
      ),
    );
    const { stream, container } = await openList({ items: [item] });

    fireEvent.click(cancelIn('Crawl'));

    expect(await within(rowOf('Crawl')).findByText('Cancelling')).toBeVisible();
    expect(within(rowOf('Crawl')).queryByRole('button')).toBeNull();
    await on(stream, 'a', workItem(1, { status: 'Cancelled', updatedAt: '2026-11-12T12:11:00Z' }));
    expect(await within(rowOf('Crawl')).findByText('Cancelled')).toBeVisible();
    expect(await axeResults(container)).toHaveNoViolations();
  });

  it('cancels a queued item at once', async () => {
    const item = workItem(1);
    server.use(
      cancelAnswers((id) =>
        id === item.id
          ? HttpResponse.json({ ...item, status: 'Cancelled', updatedAt: '2026-11-12T12:10:00Z' })
          : problemResponse(404, 'missing'),
      ),
    );
    await openList({ items: [item] });

    fireEvent.click(cancelIn('Crawl'));

    expect(await within(rowOf('Crawl')).findByText('Cancelled')).toBeVisible();
  });

  it('shows how an item that finished meanwhile ended', async () => {
    const item = workItem(1, { status: 'Running' });
    server.use(
      cancelAnswers(() =>
        HttpResponse.json(
          { ...item, status: 'Succeeded', updatedAt: '2026-11-12T12:10:00Z' },
          { status: 409 },
        ),
      ),
    );
    await openList({ items: [item] });

    fireEvent.click(cancelIn('Crawl'));

    expect(await within(rowOf('Crawl')).findByText('Succeeded')).toBeVisible();
  });

  it('says so when it fails', async () => {
    server.use(cancelAnswers(() => problemResponse(404, 'missing')));
    await openList({ items: [workItem(1)] });

    fireEvent.click(cancelIn('Crawl'));

    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't cancel it. Try again.");
  });

  it('from the detail updates it', async () => {
    const item = workItem(1, { status: 'Running' });
    server.use(
      workItemAnswers(() => item),
      cancelAnswers(() =>
        HttpResponse.json(
          { ...item, cancelRequestedAt: '2026-11-12T12:10:00Z', updatedAt: '2026-11-12T12:10:00Z' },
          { status: 202 },
        ),
      ),
    );
    lists({ items: [] });
    renderApp(`/admin/work-items/${itemId(1)}`);

    fireEvent.click(await screen.findByRole('button', { name: /^Cancel Crawl/ }));

    expect(await screen.findByText('Cancelling')).toBeVisible();
  });
});

describe('who sees the work queue', () => {
  it('a non-admin sees not found, and no way in from Settings', async () => {
    server.use(refreshSucceeds(miraSession));
    lists({ items: [workItem(1)] });
    const { router } = renderApp('/admin/work-items');

    expect(await screen.findByRole('heading', { name: 'Page not found' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Today' })).toBeNull();
    expect(listed).toEqual([]);
    await act(() => router.navigate({ to: '/settings' }));
    expect(await screen.findByRole('heading', { name: 'Account' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'Work queue' })).toBeNull();
  });

  it('an admin reaches it from Settings', async () => {
    lists({ items: [workItem(1)] });
    renderApp('/settings');

    fireEvent.click(await screen.findByRole('link', { name: 'Work queue' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Work queue' })).toBeVisible();
    expect(await screen.findByRole('link', { name: 'Crawl' })).toBeVisible();
    fireEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(await screen.findByRole('heading', { name: 'Account' })).toBeVisible();
  });
});
