import { useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { startGroup } from './day';
import { createGestures, listenToKeys } from './navigation';
import { createStory } from './story';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const [{ snapshot, story, gestures, subscribe }] = useState(() => {
    const story = createStory(
      years.map((group) => group.memories.map((memory) => memory.proxies.preview !== null)),
      startGroup(years, year),
      close,
    );
    const subscribe = (listener: () => void) => {
      const stopKeys = listenToKeys(story);
      const stop = story.subscribe(listener);
      return () => {
        stop();
        stopKeys();
      };
    };
    return { snapshot: years, story, gestures: createGestures(story), subscribe };
  });
  const position = useSyncExternalStore(subscribe, story.position);
  const group = snapshot[position.group] as YearGroup;
  return {
    position,
    group,
    memory: group.memories[position.memory] as Memory,
    mediaReady: () => {
      story.mediaReady(position.visit);
    },
    gestures,
  };
}
