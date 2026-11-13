import { useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { startGroup } from './day';
import { createGestures, listenToInput } from './navigation';
import { createStory, type Place } from './story';
import { storyView } from './story-view';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const [{ snapshot, story, gestures, subscribe }] = useState(() => {
    const story = createStory(
      years.map((group) => group.memories.map((memory) => memory.proxies.preview !== null)),
      startGroup(years, year),
      close,
    );
    const subscribe = (listener: () => void) => {
      const stopInput = listenToInput(story);
      const stop = story.subscribe(listener);
      return () => {
        stop();
        stopInput();
      };
    };
    return { snapshot: years, story, gestures: createGestures(story), subscribe };
  });
  const state = useSyncExternalStore(subscribe, story.state);
  const group = snapshot[state.group] as YearGroup;
  return {
    state,
    group,
    ...storyView(state, group),
    memoryAt: ({ group, memory }: Place) =>
      (snapshot[group] as YearGroup).memories[memory] as Memory,
    hide: (place?: Place) => story.hide(place),
    undo: (place: Place) => {
      story.undo(place);
    },
    mediaReady: () => {
      story.mediaReady(state.visit);
    },
    gestures,
    togglePause: story.togglePause,
  };
}
