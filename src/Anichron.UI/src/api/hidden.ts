import type { Client } from 'openapi-fetch';
import type { paths } from './generated/v1';
import type { Memory, ShowMemories } from './memories';
import type { Authorized } from './session';

export interface HiddenMemory {
  memory: Memory;
  hiddenAt: string;
}

export interface HiddenPage {
  items: HiddenMemory[];
  next: string | null;
}

export interface Hidden {
  page(after: string | null, signal?: AbortSignal): Promise<HiddenPage>;
}

export function createHidden(
  client: Client<paths>,
  authorized: Authorized,
  show: ShowMemories,
): Hidden {
  return {
    async page(after, signal) {
      const page = await authorized((init) =>
        client.GET('/api/v1/assets/hidden', {
          ...init,
          signal,
          params: { query: { after: after ?? undefined } },
        }),
      );
      return show((memoryOf) => ({
        items: page.items.map(({ asset, hiddenAt }) => ({ memory: memoryOf(asset), hiddenAt })),
        next: page.next,
      }));
    },
  };
}
