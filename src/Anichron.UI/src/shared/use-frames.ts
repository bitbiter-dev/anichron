import type { PanInfo } from 'motion/react';
import { type UIEvent, useEffect, useState } from 'react';
import { usePress } from 'react-aria/usePress';
import { clampFrame, createFrameScroll, swipeOf } from './frames';
import { listen } from './listen';

function frameStep(key: string) {
  const steps: Partial<Record<string, number>> = { ArrowLeft: -1, ArrowRight: 1 };
  return steps[key];
}

export type FrameViewer = ReturnType<typeof useFrames>;

export function useFrames(count: number, close: () => void, start = 0) {
  const [chosen, setChosen] = useState(start);
  const [scroll] = useState(() => createFrameScroll(start));
  const current = clampFrame(chosen, count);

  function moveBy(step: number) {
    const to = clampFrame(current + step, count);
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
    current,
    place: current + 1,
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
      if (swipeOf(offset, velocity, innerHeight) === 'down') close();
    },
  };
}
