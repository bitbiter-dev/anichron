import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import type { Memory } from '../../api/flashbacks';
import { blurhashOf } from '../../shared/blurhash';
import { shownSource } from '../../shared/media';
import { useFrames } from '../../shared/use-frames';
import { framesShown } from './burst';
import { burstQuery } from './day';

export function useFrameBlurhash(frame: Memory) {
  const [decoded, setDecoded] = useState(false);
  return {
    blurhash: decoded ? null : blurhashOf(frame),
    onDecoded: () => {
      setDecoded(true);
    },
  };
}

export function useBurstDetails(cover: Memory, close: () => void) {
  const { api } = useRouter().options.context;
  const { downloads } = api.media;
  const query = useQuery(burstQuery(api, cover));
  const frames = framesShown(query.data) ?? [cover];
  const viewer = useFrames(frames.length, close);

  return {
    viewer,
    frames: frames.map((memory, index) => {
      const url = memory.proxies.preview;
      return { memory, place: index + 1, src: url && shownSource(downloads, url) };
    }),
    count: frames.length,
    frame: frames[viewer.current] as Memory,
    loaded: query.isSuccess,
    failed: query.isError,
    retry: () => void query.refetch(),
  };
}
