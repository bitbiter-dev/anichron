import { useRouter } from '@tanstack/react-router';
import { useEffect, useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { previewOf, startGroup } from './day';
import { createGestures, listenToInput } from './navigation';
import { createPreload, createPreviews, nextVideo, wantedOf } from './prefetch';
import { createStory, type Place } from './story';
import { kindOf, storyView } from './story-view';
import { createVideoPlayer } from './video';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const { api, queryClient } = useRouter().options.context;
  const { session, media } = api;
  const { downloads } = media;
  const [{ snapshot, story, gestures, player, subscribe, sync, memoryAt, preload }] = useState(
    () => {
      const memoryAt = ({ group, memory }: Place) =>
        (years[group] as YearGroup).memories[memory] as Memory;
      const previewAt = (place: Place) => previewOf(api, queryClient, memoryAt(place));
      const previews = createPreviews(downloads, previewAt);
      const story = createStory(
        years.map((group) => group.memories.map(kindOf)),
        startGroup(years, year),
        close,
        previews.downloaded,
      );
      const player = createVideoPlayer(story, memoryAt, () => session.renewMedia(), downloads);
      const sync = () => {
        const state = story.state();
        downloads.keep(wantedOf(state, previewAt, memoryAt));
        previews.sync(state, story.mediaFailed);
      };
      const subscribe = (listener: () => void) => {
        const stopInput = listenToInput(story);
        const stopStory = story.subscribe(() => {
          sync();
          player.sync();
          listener();
        });
        const stopDownloads = downloads.subscribe(() => {
          player.sync();
          story.mediaProgressed();
        });
        return () => {
          stopStory();
          // Stryker disable next-line CallExpression
          stopDownloads();
          stopInput();
          downloads.clear();
        };
      };
      return {
        snapshot: years,
        story,
        gestures: createGestures(story),
        player,
        subscribe,
        sync,
        memoryAt,
        preload: createPreload(),
      };
    },
  );
  const state = useSyncExternalStore(subscribe, story.state);
  const arrived = useSyncExternalStore(downloads.subscribe, downloads.snapshot);
  useEffect(sync);
  const group = snapshot[state.group] as YearGroup;
  return {
    state,
    group,
    ...storyView(state, group),
    nextVideo: preload(nextVideo(state, memoryAt), arrived.picturesSettled),
    source: arrived.sourceOf,
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
    closeDetails: story.closeDetails,
    togglePause: story.togglePause,
    toggleSound: story.toggleSound,
    replay: story.replay,
  };
}
