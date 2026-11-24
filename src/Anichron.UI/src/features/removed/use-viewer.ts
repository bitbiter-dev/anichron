import { useEffect } from 'react';
import { useFrames } from '../../shared/use-frames';
import { slidesOf } from '../../shared/use-slide';
import type { RemovedRow } from './removed';
import type { RemovedScreen } from './use-removed';

export function useViewer(screen: RemovedScreen, start: number) {
  const { rows, nearing } = screen;
  const viewer = useFrames(rows.length, screen.close, start);
  const { current } = viewer;
  const row = rows[current] as RemovedRow;

  useEffect(() => {
    nearing(current);
  });

  return {
    viewer,
    slides: slidesOf(
      rows.map(({ memory }) => memory),
      current,
    ),
    caption: { path: row.memory.path, reason: row.reason },
  };
}
