import type { Client } from 'openapi-fetch';
import { type Memory, memoryOf } from './flashbacks';
import type { paths } from './generated/v1';
import type { Authorized } from './session';

export interface Bursts {
  frames(id: string): Promise<Memory[]>;
}

export function createBursts(client: Client<paths>, authorized: Authorized): Bursts {
  return {
    async frames(id) {
      const frames = await authorized((init) =>
        client.GET('/api/v1/bursts/{burstId}/assets', {
          ...init,
          params: { path: { burstId: id } },
        }),
      );
      return frames.map(memoryOf);
    },
  };
}
