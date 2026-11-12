import { useEffect, useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { startGroup } from './day';
import { createGestures, keyMove } from './navigation';
import { createStory } from './story';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const [{ snapshot, story, gestures }] = useState(() => {
    const story = createStory(
      years.map((group) => group.memories.map((memory) => memory.proxies.preview !== null)),
      startGroup(years, year),
      close,
    );
    return { snapshot: years, story, gestures: createGestures(story) };
  });
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const move = keyMove(event);
      if (move) story[move]();
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
    };
  }, [story]);
  const position = useSyncExternalStore(story.subscribe, story.position);
  const group = snapshot[position.group] as YearGroup;
  return {
    position,
    group,
    memory: group.memories[position.memory] as Memory,
    story,
    gestures,
  };
}
