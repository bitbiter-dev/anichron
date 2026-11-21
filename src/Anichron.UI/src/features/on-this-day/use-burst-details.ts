import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { PanInfo } from 'motion/react';
import { type UIEvent, useEffect, useState, useSyncExternalStore } from 'react';
import { usePress } from 'react-aria/usePress';
import type { Memory } from '../../api/flashbacks';
import { listen } from '../../shared/listen';
import { blurhashOf } from './blurhash';
import { clampFrame, createFrameScroll, framesShown } from './burst';
import { burstQuery } from './day';
import { closesOnSwipe } from './navigation';

function frameStep(key: string) {
  const steps: Partial<Record<string, number>> = { ArrowLeft: -1, ArrowRight: 1 };
  return steps[key];
}

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
  const arrived = useSyncExternalStore(downloads.subscribe, downloads.snapshot);
  const query = useQuery(burstQuery(api, cover));
  const frames = framesShown(query.data) ?? [cover];
  const [chosen, setChosen] = useState(0);
  const [scroll] = useState(createFrameScroll);
  const current = clampFrame(chosen, frames.length);

  function moveBy(step: number) {
    const to = clampFrame(current + step, frames.length);
    scroll.scrollTo(to);
    setChosen(to);
  }

  useEffect(() =>
    listen(document, 'keydown', (event) => {
      const step = frameStep((event as KeyboardEvent).key);
      if (step) moveBy(step);
    }),
  );

  const { pressProps } = usePress({
    onPressStart: scroll.pressStart,
    onPress: ({ x }) => {
      if (scroll.tapped()) moveBy(x < innerWidth / 2 ? -1 : 1);
    },
  });

  return {
    frames: frames.map((memory, index) => {
      const url = memory.proxies.preview;
      const src = arrived.sourceOf(url) ?? (downloads.failed(url) ? url : null);
      return { memory, place: index + 1, src };
    }),
    count: frames.length,
    frame: frames[current] as Memory,
    place: current + 1,
    loaded: query.isSuccess,
    failed: query.isError,
    retry: () => void query.refetch(),
    attach: scroll.attach,
    pressProps,
    moveBy: (step: number) => () => {
      moveBy(step);
    },
    onScroll: ({ currentTarget }: UIEvent<HTMLElement>) => {
      const { scrollLeft, clientWidth } = currentTarget;
      if (clientWidth > 0) setChosen(Math.round(scrollLeft / clientWidth));
    },
    onDragStart: scroll.dragStart,
    onDragEnd: (_: unknown, { offset, velocity }: PanInfo) => {
      if (closesOnSwipe(offset, velocity, innerHeight)) close();
    },
  };
}
