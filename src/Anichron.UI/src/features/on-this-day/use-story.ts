import { useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { startGroup } from './day';
import { createStory } from './story';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const [{ snapshot, story }] = useState(() => ({
    snapshot: years,
    story: createStory(
      years.map((group) => group.memories.length),
      startGroup(years, year),
      close,
    ),
  }));
  const position = useSyncExternalStore(story.subscribe, story.position);
  const group = snapshot[position.group] as YearGroup;
  return {
    position,
    group,
    memory: group.memories[position.memory] as Memory,
    mediaReady: story.mediaReady,
  };
}
