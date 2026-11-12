import { useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { startGroup } from './day';
import { createGestures, listenToKeys } from './navigation';
import { createStory, type Place } from './story';

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
  const memoryAt = ({ group, memory }: Place) =>
    (snapshot[group] as YearGroup).memories[memory] as Memory;
  return {
    position,
    group: snapshot[position.group] as YearGroup,
    memory: memoryAt(position),
    segments: { count: position.shown.length, current: position.shown.indexOf(position.memory) },
    memoryAt,
    hide: (place?: Place) => story.hide(place),
    undo: (place: Place) => {
      story.undo(place);
    },
    mediaReady: () => {
      story.mediaReady(position.visit);
    },
    gestures,
  };
}
