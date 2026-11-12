import { createOpenApiHttp } from 'openapi-msw';
import type { components, paths } from '../../api/generated/v1';

type Schemas = components['schemas'];
type WorkItem = Schemas['WorkItemDto'];

const http = createOpenApiHttp<paths>();

export const itemId = (n: number) => `0199a1b2-c3d4-7e5f-8a9b-${String(n).padStart(12, '0')}`;

export function workItem(n: number, overrides: Partial<WorkItem> = {}): WorkItem {
  return {
    id: itemId(n),
    kind: 'Crawl',
    storageConfigId: null,
    requestedBy: null,
    status: 'Queued',
    attempt: 0,
    stalls: 0,
    claimedBy: null,
    requestedAt: `2026-11-12T12:${String(n).padStart(2, '0')}:00Z`,
    startedAt: null,
    finishedAt: null,
    updatedAt: `2026-11-12T12:${String(n).padStart(2, '0')}:00Z`,
    cancelRequestedAt: null,
    progress: { done: 0, total: null, note: null },
    result: null,
    resultIsPartial: false,
    error: null,
    ...overrides,
  };
}

export interface Listed {
  page: number;
  bearer: string | null;
}

export function workItemPages(
  pages: (
    page: number,
  ) =>
    Partial<Schemas['WorkItemPageDto']> | Response | Promise<Partial<Schemas['WorkItemPageDto']>>,
  listed: Listed[] = [],
) {
  return http.get('/api/v1/admin/work-items', async ({ request, response }) => {
    const page = Number(new URL(request.url).searchParams.get('page'));
    listed.push({ page, bearer: request.headers.get('Authorization') });
    const answer = await pages(page);
    return answer instanceof Response
      ? response.untyped(answer)
      : response(200).json({
          items: [],
          page,
          pageSize: 50,
          total: answer.items?.length ?? 0,
          cursor: '2026-11-12T12:00:00Z',
          ...answer,
        });
  });
}

export function workItemAnswers(answer: (id: string) => WorkItem | Response | Promise<WorkItem>) {
  return http.get('/api/v1/admin/work-items/{workItemId}', async ({ params, response }) => {
    if (params.workItemId === 'events') return undefined;
    const item = await answer(params.workItemId);
    return item instanceof Response ? response.untyped(item) : response(200).json(item);
  });
}

export function cancelAnswers(answer: (id: string) => Response) {
  return http.post('/api/v1/admin/work-items/{workItemId}/cancel', ({ params, response }) =>
    response.untyped(answer(params.workItemId)),
  );
}

export interface Connection {
  lastEventId: string | null;
  bearer: string | null;
  send(id: string, item: WorkItem): void;
  write(text: string): void;
  end(): void;
  drop(): void;
}

const encoder = new TextEncoder();

export function workItemEvents(refusals: ((() => Response) | undefined)[] = []) {
  const connections: Connection[] = [];
  const handler = http.get('/api/v1/admin/work-items/events', ({ request, response }) => {
    const refusal = refusals.shift();
    if (refusal) {
      connections.push({
        ...unopened,
        lastEventId: request.headers.get('Last-Event-ID'),
        bearer: request.headers.get('Authorization'),
      });
      return response.untyped(refusal());
    }
    let controller!: ReadableStreamDefaultController<Uint8Array>;
    const connection: Connection = {
      lastEventId: request.headers.get('Last-Event-ID'),
      bearer: request.headers.get('Authorization'),
      send(id, item) {
        this.write(`event: work-item\nid: ${id}\ndata: ${JSON.stringify(item)}\n\n`);
      },
      write(text) {
        controller.enqueue(encoder.encode(text));
      },
      end() {
        controller.close();
      },
      drop() {
        controller.error(new TypeError('network error'));
      },
    };
    const body = new ReadableStream<Uint8Array>({
      start(started) {
        controller = started;
      },
    });
    connections.push(connection);
    return response.untyped(
      new Response(body, { headers: { 'Content-Type': 'text/event-stream' } }),
    );
  });
  return { handler, connections };
}

const unopened: Connection = {
  lastEventId: null,
  bearer: null,
  send: () => undefined,
  write: () => undefined,
  end: () => undefined,
  drop: () => undefined,
};
