import type { Memory } from '../api/memories';
import { blurhashOf } from './blurhash';

export type Load = 'loading' | 'shown' | 'failed';

export type Kind = 'unready' | 'picture' | 'video' | 'live' | 'burst';

type ImageShown = 'picture' | 'poster' | null;

type Name = 'story.photo' | 'story.video' | 'story.livePhoto' | 'story.burst';

interface Video {
  source: (memory: Memory, load: Load) => string | null;
  live: boolean;
}

export interface MemoryKind {
  firstLoad: (decoded: boolean) => Load;
  image: (load: Load) => ImageShown;
  video: Video | null;
  failure: boolean;
  liveButton: boolean;
  sound: boolean;
  name: (memory: Memory) => Name;
}

export function kindOf({ proxies, livePhotoVideoUrl, burst }: Memory): Kind {
  if (proxies.video) return 'video';
  if (!proxies.preview) return 'unready';
  if (burst) return 'burst';
  if (livePhotoVideoUrl) return 'live';
  return 'picture';
}

function nameOf({ mediaType }: Memory) {
  const names: Record<Memory['mediaType'], Name> = {
    Image: 'story.photo',
    Video: 'story.video',
    LivePhoto: 'story.livePhoto',
  };
  return names[mediaType];
}

export function memoryKind(kind: Kind): MemoryKind {
  const picture: MemoryKind = {
    firstLoad: (decoded) => (decoded ? 'shown' : 'loading'),
    image: (load) => (load === 'failed' ? null : 'picture'),
    video: null,
    failure: true,
    liveButton: false,
    sound: false,
    name: nameOf,
  };
  const kinds: Record<Kind, MemoryKind> = {
    unready: { ...picture, firstLoad: () => 'shown' },
    picture,
    video: {
      ...picture,
      firstLoad: () => 'loading',
      image: (load) => (load === 'failed' ? 'poster' : null),
      video: {
        source: ({ proxies }, load) => (load === 'failed' ? null : proxies.video),
        live: false,
      },
      failure: false,
      sound: true,
    },
    live: {
      ...picture,
      video: { source: ({ livePhotoVideoUrl }) => livePhotoVideoUrl, live: true },
      liveButton: true,
    },
    burst: { ...picture, name: () => 'story.burst' },
  };
  return kinds[kind];
}

export function loadView(kind: MemoryKind, memory: Memory, load: Load) {
  return {
    blurhash: load === 'shown' ? null : blurhashOf(memory),
    failed: kind.failure && load === 'failed',
    liveButton: kind.liveButton && load === 'shown',
  };
}

export function soundLabel(muted: boolean): 'story.soundOn' | 'story.soundOff' {
  return muted ? 'story.soundOn' : 'story.soundOff';
}
