import type { Client } from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import type { Shown, ShowMemories } from './memories';
import type { Authorized, Session } from './session';

export type RemovedMemory = Shown<components['schemas']['SoftDeletedAssetDto']>;

export interface RemovedPage {
  items: RemovedMemory[];
  next: string | null;
  total: number;
}

export interface SoftDeleted {
  page(after: string | null, signal?: AbortSignal): Promise<RemovedPage>;
  purge(ids: readonly string[]): Promise<string[]>;
}

export function createSoftDeleted(
  client: Client<paths>,
  authorized: Authorized,
  show: ShowMemories,
  session: Pick<Session, 'onUserChange'>,
): SoftDeleted {
  const forgotten = new Set<string>();
  session.onUserChange(() => {
    forgotten.clear();
  });
  return {
    async page(after, signal) {
      const page = await authorized((init) =>
        client.GET('/api/v1/assets/soft-deleted', {
          ...init,
          signal,
          params: { query: { after: after ?? undefined } },
        }),
      );
      return show((memoryOf) => ({
        items: page.items.filter(({ id }) => !forgotten.has(id)).map(memoryOf),
        next: page.next,
        total: page.total,
      }));
    },
    async purge(ids) {
      const { accepted } = await authorized((init) =>
        client.POST('/api/v1/assets/purges', { ...init, body: { assetIds: [...ids] } }),
      );
      for (const id of accepted) forgotten.add(id);
      return accepted;
    },
  };
}
