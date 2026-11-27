import type { Client } from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import type { Shown, ShowMemories } from './memories';
import type { Authorized } from './session';

export type RemovedMemory = Shown<components['schemas']['SoftDeletedAssetDto']>;

export interface RemovedPage {
  items: RemovedMemory[];
  next: string | null;
}

export interface SoftDeleted {
  page(after: string | null, signal?: AbortSignal): Promise<RemovedPage>;
}

export function createSoftDeleted(
  client: Client<paths>,
  authorized: Authorized,
  show: ShowMemories,
): SoftDeleted {
  return {
    async page(after, signal) {
      const page = await authorized((init) =>
        client.GET('/api/v1/assets/soft-deleted', {
          ...init,
          signal,
          params: { query: { after: after ?? undefined } },
        }),
      );
      return show((memoryOf) => ({ items: page.items.map(memoryOf), next: page.next }));
    },
  };
}
