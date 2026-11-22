import { useEffect } from 'react';
import type { HiddenMemory } from '../../api/hidden';
import { useFrames } from '../../shared/use-frames';
import { hiddenAgo, takenFormat, takenOf } from './hidden';
import type { HiddenScreen } from './use-hidden';

export function useViewer(screen: HiddenScreen, start: number) {
  const { items, nearing } = screen;
  const viewer = useFrames(items.length, screen.close, start);
  const { current } = viewer;
  const item = items[current] as HiddenMemory;

  useEffect(() => {
    nearing(current);
  });

  return {
    viewer,
    slides: items.map(({ memory }, index) => ({
      memory,
      current: index === current,
      shown: Math.abs(index - current) <= 1,
    })),
    caption: {
      taken: takenOf(item.memory),
      hidden: hiddenAgo(item.hiddenAt, new Date()).value,
      formatParams: {
        ...takenFormat(),
        hidden: { range: hiddenAgo(item.hiddenAt, new Date()).range, numeric: 'auto' },
      },
    },
    unhide: () => {
      screen.unhide(item);
    },
  };
}
