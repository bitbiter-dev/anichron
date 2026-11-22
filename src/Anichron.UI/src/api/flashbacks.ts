import type { Client } from 'openapi-fetch';
import type { components, paths } from './generated/v1';
import type { Authorized } from './session';

type Schemas = components['schemas'];
type Proxies = Omit<Schemas['AssetProxyUrls'], 'compactPreview'>;

export type Memory = Omit<Schemas['AssetDto'], 'proxies'> & { proxies: Proxies };

export interface YearGroup {
  year: number;
  memories: Memory[];
}

export interface Day {
  years: YearGroup[];
}

export interface Flashbacks {
  onThisDay(date: string, signal?: AbortSignal): Promise<Day>;
}

function fromApi(url: string | null) {
  return url && `/api/v1/${url}`;
}

export function memoryOf(asset: Schemas['AssetDto'], compact: boolean): Memory {
  const { compactPreview, ...proxies } = asset.proxies;
  const shown = {
    ...proxies,
    preview: compact ? (compactPreview ?? proxies.preview) : proxies.preview,
  };
  return {
    ...asset,
    proxies: Object.fromEntries(
      Object.entries(shown).map(([type, url]) => [type, fromApi(url)]),
    ) as Proxies,
    livePhotoVideoUrl: fromApi(asset.livePhotoVideoUrl),
  };
}

export function createFlashbacks(
  client: Client<paths>,
  authorized: Authorized,
  compact: Promise<boolean>,
): Flashbacks {
  return {
    async onThisDay(date, signal) {
      const day = await authorized((init) =>
        client.GET('/api/v1/flashbacks/on-this-day', {
          ...init,
          signal,
          params: { query: { date } },
        }),
      );
      const shown = await compact;
      return {
        years: day.years.map(({ year, assets }) => ({
          year,
          memories: assets.map((asset) => memoryOf(asset, shown)),
        })),
      };
    },
  };
}
