import type { Client } from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import type { Authorized } from './session';

type Schemas = components['schemas'];

export type Flag = keyof Schemas['PatchInteractionRequest'];

export type Change = Partial<Record<Flag, boolean>>;

export interface Assets {
  interact(id: string, change: Change): Promise<Schemas['AssetInteractionDto']>;
}

export function createAssets(client: Client<paths>, authorized: Authorized): Assets {
  return {
    interact(id, change) {
      return authorized((init) =>
        client.PATCH('/api/v1/assets/{assetId}/interaction', {
          ...init,
          params: { path: { assetId: id } },
          body: change as Schemas['PatchInteractionRequest'],
        }),
      );
    },
  };
}
