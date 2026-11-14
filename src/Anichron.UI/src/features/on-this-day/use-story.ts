import { useRouter } from '@tanstack/react-router';
import { useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { startGroup } from './day';
import { createGestures, listenToInput } from './navigation';
import { createPrefetch, nextVideo } from './prefetch';
import { createStory, type Place } from './story';
import { kindOf, storyView } from './story-view';
import { createVideoPlayer } from './video';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const { session, media } = useRouter().options.context.api;
  const [{ snapshot, story, gestures, player, subscribe, memoryAt }] = useState(() => {
    const memoryAt = ({ group, memory }: Place) =>
      (years[group] as YearGroup).memories[memory] as Memory;
    const story = createStory(
      years.map((group) => group.memories.map(kindOf)),
      startGroup(years, year),
      close,
    );
    const player = createVideoPlayer(story, memoryAt, () => session.renewMedia());
    const prefetch = createPrefetch(memoryAt, (url) => {
      media.prefetchPicture(url);
    });
    const subscribe = (listener: () => void) => {
      const stopInput = listenToInput(story);
      prefetch(story.state());
      const stop = story.subscribe(() => {
        player.sync();
        prefetch(story.state());
        listener();
      });
      return () => {
        stop();
        stopInput();
      };
    };
    return {
      snapshot: years,
      story,
      gestures: createGestures(story),
      player,
      subscribe,
      memoryAt,
    };
  });
  const state = useSyncExternalStore(subscribe, story.state);
  const group = snapshot[state.group] as YearGroup;
  return {
    state,
    group,
    ...storyView(state, group),
    memoryAt,
    nextVideo: nextVideo(state, memoryAt),
    hide: (place?: Place) => story.hide(place),
    undo: (place: Place) => {
      story.undo(place);
    },
    mediaReady: () => {
      story.mediaReady(state.visit);
    },
    mediaFailed: () => {
      story.mediaFailed(state.visit);
    },
    gestures,
    videoEvents: player.events,
    togglePause: story.togglePause,
    toggleSound: story.toggleSound,
  };
}
