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
    const gestures = createGestures(story);
    const subscribe = (listener: () => void) => {
      const stopKeys = listenToKeys(story);
      const stop = story.subscribe(listener);
      return () => {
        stop();
        stopKeys();
        gestures.stop();
      };
    };
    return { snapshot: years, story, gestures, subscribe };
  });
  const position = useSyncExternalStore(subscribe, story.state);
  const group = snapshot[position.group] as YearGroup;
  return {
    position,
    group,
    memory: group.memories[position.memory] as Memory,
    mediaReady: () => {
      story.mediaReady(position.visit);
    },
    gestures,
    togglePause: story.togglePause,
  };
}
