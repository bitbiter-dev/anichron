import { useState } from 'react';
import type { Memory } from '../../api/flashbacks';
import { blurhashOf } from '../../shared/blurhash';
import { kindOf, type Load, memoryKind } from '../../shared/memory-kinds';
import { useRecovering } from '../../shared/use-recovering';

export interface Slide {
  memory: Memory;
  shown: boolean;
  current: boolean;
}

export function useSlide({ memory, shown, current }: Slide) {
  const kind = memoryKind(kindOf(memory));
  const [load, setLoad] = useState<Load>(() => kind.firstLoad(() => false));
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

  return {
    name: kind.name(memory),
    reload: `${String(video.reloads)}-${String(replays)}`,
    blurhash: load === 'shown' ? null : blurhashOf(memory),
    picture: {
      src: image && preview,
      onDecoded: () => {
        if (image === 'picture') setLoad('shown');
      },
      onFailed: () => {
        if (image === 'picture') setLoad('failed');
      },
    },
    failed: kind.failure && load === 'failed',
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
            label: muted ? ('story.soundOn' as const) : ('story.soundOff' as const),
            toggle: () => {
              setMuted(!muted);
            },
          }
        : null,
    live: source && plays ? ('playing' as const) : ('still' as const),
    replay:
      current && kind.liveButton && load === 'shown'
        ? () => {
            setLive(true);
            setReplays(replays + 1);
          }
        : null,
  } as const;
}
