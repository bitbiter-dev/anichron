import type { components } from './generated/v1';

type Asset = components['schemas']['AssetDto'];
type SoftDeletedAsset = components['schemas']['SoftDeletedAssetDto'];
type ApiMedia = Asset | SoftDeletedAsset;
type Proxies = Omit<Asset['proxies'], 'compactPreview'>;

export type Shown<A extends ApiMedia> = Omit<A, 'proxies'> & { proxies: Proxies };

export type Memory = Shown<Asset>;

export type MemoryMedia = Pick<
  Memory,
  'id' | 'mediaType' | 'width' | 'height' | 'blurhash' | 'proxies' | 'livePhotoVideoUrl'
> &
  Partial<Pick<Memory, 'burst'>>;

export type MemoryOf = <A extends ApiMedia>(asset: A) => Shown<A>;

export type ShowMemories = <T>(map: (memoryOf: MemoryOf) => T) => Promise<T>;

function fromApi(url: string | null) {
  return url && `/api/v1/${url}`;
}

function memoryOf<A extends ApiMedia>(asset: A, compact: boolean): Shown<A> {
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
