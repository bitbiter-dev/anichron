import type { Client } from 'openapi-fetch';
import type { paths } from './generated/v1';
import type { Memory, ShowMemories } from './memories';
import type { Authorized } from './session';

export interface Bursts {
  frames(id: string): Promise<Memory[]>;
}

export function createBursts(
  client: Client<paths>,
  authorized: Authorized,
  show: ShowMemories,
): Bursts {
  return {
    async frames(id) {
      const frames = await authorized((init) =>
        client.GET('/api/v1/bursts/{burstId}/assets', {
          ...init,
          params: { path: { burstId: id } },
        }),
      );
      return show((memoryOf) => frames.map(memoryOf));
    },
  };
}
