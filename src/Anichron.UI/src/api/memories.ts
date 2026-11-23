import type { components } from './generated/v1';

type Asset = components['schemas']['AssetDto'];
type Proxies = Omit<Asset['proxies'], 'compactPreview'>;

export type Memory = Omit<Asset, 'proxies'> & { proxies: Proxies };

export type ShowMemories = <T>(map: (memoryOf: (asset: Asset) => Memory) => T) => Promise<T>;

function fromApi(url: string | null) {
  return url && `/api/v1/${url}`;
}

function memoryOf(asset: Asset, compact: boolean): Memory {
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

export function createShowMemories(compact: Promise<boolean>): ShowMemories {
  return async (map) => {
    const shown = await compact;
    return map((asset) => memoryOf(asset, shown));
  };
}
