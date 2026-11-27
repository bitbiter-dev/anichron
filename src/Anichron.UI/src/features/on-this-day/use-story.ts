import { useRouter } from '@tanstack/react-router';
import { useEffect, useState, useSyncExternalStore } from 'react';
import type { YearGroup } from '../../api/flashbacks';
import type { Memory } from '../../api/memories';
import { framesShown } from './burst';
import { framesOf, previewOf, standInOf, startGroup } from './day';
import { createGestures, listenToInput } from './navigation';
import { kindOf } from '../../shared/memory-kinds';
import { createPreload, createPreviews, nextVideo, wantedOf } from './prefetch';
import { createStory, type Place } from './story';
import { storyView } from './story-view';
import { createVideoPlayer } from './video';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const { api, queryClient } = useRouter().options.context;
  const { media } = api;
  const { downloads } = media;
  const [{ snapshot, story, gestures, player, subscribe, sync, memoryAt, preload }] = useState(
    () => {
      const memoryAt = ({ group, memory }: Place) =>
        (years[group] as YearGroup).memories[memory] as Memory;
      const previewAt = (place: Place) => previewOf(api, queryClient, memoryAt(place));
      const framesAt = (place: Place) =>
        framesShown(framesOf(api, queryClient, memoryAt(place))) ?? [];
      const previews = createPreviews(downloads, previewAt);
      const story = createStory(
        years.map((group) =>
          group.memories.map((memory) => ({
            kind: kindOf(memory),
            durationSeconds: memory.durationSeconds,
          })),
        ),
        startGroup(years, year),
        close,
        previews.downloaded,
      );
      const player = createVideoPlayer(story, memoryAt, media.renewOnce, downloads);
      const sync = () => {
        const state = story.state();
        downloads.keep(wantedOf(state, previewAt, memoryAt, framesAt));
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
    standIn: standInOf(api, queryClient, memoryAt(state), state.load),
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
