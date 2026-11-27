import { useState } from 'react';
import type { MemoryMedia } from '../api/memories';
import { kindOf, type Load, loadView, memoryKind, soundLabel } from './memory-kinds';
import { useRecovering } from './use-recovering';

export interface Slide {
  memory: MemoryMedia;
  shown: boolean;
  current: boolean;
}

export function slidesOf(memories: readonly MemoryMedia[], current: number): Slide[] {
  return memories.map((memory, index) => ({
    memory,
    current: index === current,
    shown: Math.abs(index - current) <= 1,
  }));
}

export function useSlide({ memory, shown, current }: Slide) {
  const kind = memoryKind(kindOf(memory));
  const [load, setLoad] = useState<Load>(() => kind.firstLoad(false));
  const [live, setLive] = useState(true);
  const [replays, setReplays] = useState(0);
  const [muted, setMuted] = useState(true);
  const video = useRecovering(() => {
    setLoad('failed');
  });
  if (!current && !live) setLive(true);
  const image = kind.image(load);
  const source = current ? kind.video?.source(memory, load) : null;
  const plays = kind.video?.live ? live && load === 'shown' : true;
  const preview = shown ? memory.proxies.preview : null;
  const { blurhash, failed, liveButton } = loadView(kind, memory, load);

  return {
    name: kind.name(memory),
    reload: `${String(video.reloads)}-${String(replays)}`,
    blurhash,
    picture: {
      src: image && preview,
      onDecoded: () => {
        if (image === 'picture') setLoad('shown');
      },
      onFailed: () => {
        if (image === 'picture') setLoad('failed');
      },
    },
    failed,
    video:
      source && plays
        ? {
            src: source,
            poster: memory.proxies.preview ?? undefined,
            muted: muted || kind.video?.live === true,
            onPlaying: () => {
              setLoad('shown');
            },
            onEnded: () => {
              setLive(false);
            },
            onError: () => {
              if (kind.video?.live) setLive(false);
              else video.onError(source);
            },
          }
        : null,
    sound:
      current && kind.sound
        ? {
            label: soundLabel(muted),
            toggle: () => {
              setMuted(!muted);
            },
          }
        : null,
    live: source && plays ? ('playing' as const) : ('still' as const),
    replay:
      current && liveButton
        ? () => {
            setLive(true);
            setReplays(replays + 1);
          }
        : null,
  } as const;
}
