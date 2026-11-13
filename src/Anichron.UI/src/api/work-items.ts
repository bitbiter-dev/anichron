import type { Client } from 'openapi-fetch';
import type { Received } from './event-stream';
import type { components, paths } from './generated/v1';
import type { Authorized, Call } from './session';

type Schemas = components['schemas'];
export type WorkItem = Schemas['WorkItemDto'];
export type WorkItemPage = Schemas['WorkItemPageDto'];
export type WorkKind = Schemas['WorkKind'];
export type WorkItemStatus = Schemas['WorkItemStatus'];

export type Following = Received<WorkItem>;

export interface WorkItems {
  list(page: number): Promise<WorkItemPage>;
  get(id: string): Promise<WorkItem | null>;
  cancel(id: string): Promise<WorkItem>;
  follow(lastEventId: string, signal: AbortSignal, following: Following): Promise<void>;
}

export function createWorkItems(client: Client<paths>, authorized: Authorized): WorkItems {
  async function unlessGone<T>(call: Parameters<Authorized>[0]): Promise<T | null> {
    const gone = [403, 404];
    let status = 0;
    try {
      return (await authorized(async (init) => {
        const answer = await call(init);
        status = answer.response.status;
        return answer;
      })) as T;
    } catch (error) {
      if (gone.includes(status)) return null;
      throw error;
    }
  }

  return {
    list: (page) =>
      authorized((init) =>
        client.GET('/api/v1/admin/work-items', { ...init, params: { query: { page } } }),
      ),
    get: (id) =>
      unlessGone((init) =>
        client.GET('/api/v1/admin/work-items/{workItemId}', {
          ...init,
          params: { path: { workItemId: id } },
        }),
      ),
    cancel: (id) =>
      authorized(async (init): Promise<Call<WorkItem>> =>
        settled(
          await client.POST('/api/v1/admin/work-items/{workItemId}/cancel', {
            ...init,
            params: { path: { workItemId: id } },
          }),
        ),
      ),
    async follow(lastEventId, signal, following) {
      const { follow } = await import('./event-stream');
      await follow(
        (id) =>
          unlessGone<ReadableStream<BufferSource>>((init) =>
            client.GET('/api/v1/admin/work-items/events', {
              ...init,
              params: { header: { 'Last-Event-ID': id } },
              parseAs: 'stream',
              signal,
            }),
          ),
        lastEventId,
        signal,
        {
          ...following,
          event: (id, data) => {
            following.event(id, JSON.parse(data) as WorkItem);
          },
        },
      );
    },
  };
}

function settled(call: Call<WorkItem>): Call<WorkItem> {
  return call.response.status === 409
    ? { data: call.error as WorkItem, response: call.response }
    : call;
}
