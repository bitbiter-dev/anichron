import { useEffect } from 'react';
import type { HiddenMemory } from '../../api/hidden';
import { useFrames } from '../../shared/use-frames';
import { slidesOf } from '../../shared/use-slide';
import { hiddenAgo, takenFormat, takenOf } from './hidden';
import type { HiddenScreen } from './use-hidden';

export function useViewer(screen: HiddenScreen, start: number) {
  const { items, nearing } = screen;
  const viewer = useFrames(items.length, screen.close, start);
  const { current } = viewer;
  const item = items[current] as HiddenMemory;
  const ago = hiddenAgo(item.hiddenAt, new Date());

  useEffect(() => {
    nearing(current);
  });

  return {
    viewer,
    slides: slidesOf(
      items.map(({ memory }) => memory),
      current,
    ),
    caption: {
      taken: takenOf(item.memory),
      hidden: ago.value,
      formatParams: {
        ...takenFormat(),
        hidden: { range: ago.range, numeric: 'auto' },
      },
    },
    unhide: () => {
      screen.unhide(item);
    },
  };
}
