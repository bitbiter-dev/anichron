import type { Client } from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import type { Authorized } from './session';

type Schemas = components['schemas'];
type Proxies = Schemas['AssetProxyUrls'];

export type Memory = Omit<Schemas['AssetDto'], 'livePhotoVideoUrl'>;

export interface YearGroup {
  year: number;
  memories: Memory[];
}

export interface Day {
  years: YearGroup[];
}

export interface Flashbacks {
  onThisDay(date: string): Promise<Day>;
}

const fromApi = (url: string | null) => url && `/api/v1/${url}`;

const memoryOf = (asset: Schemas['AssetDto']): Memory => ({
  ...asset,
  proxies: Object.fromEntries(
    Object.entries(asset.proxies).map(([type, url]) => [type, fromApi(url)]),
  ) as Proxies,
});

export function createFlashbacks(client: Client<paths>, authorized: Authorized): Flashbacks {
  return {
    async onThisDay(date) {
      const day = await authorized((init) =>
        client.GET('/api/v1/flashbacks/on-this-day', { ...init, params: { query: { date } } }),
      );
      return {
        years: day.years.map(({ year, assets }) => ({
          year,
          memories: assets.map(memoryOf),
        })),
      };
    },
  };
}
