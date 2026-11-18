import { useRouter } from '@tanstack/react-router';
import { useState, useSyncExternalStore } from 'react';
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
  const [{ snapshot, story, gestures, player, subscribe, memoryAt, preload }] = useState(() => {
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
    const player = createVideoPlayer(
      story,
      memoryAt,
      () => session.renewMedia(),
      (url) => downloads.sourceOf(url),
    );
    const sync = () => {
      const state = story.state();
      downloads.keep(wantedOf(state, previewAt, memoryAt));
      previews.sync(state, story.mediaFailed);
    };
    const subscribe = (listener: () => void) => {
      const stopInput = listenToInput(story);
      sync();
      const stopStory = story.subscribe(() => {
        sync();
        player.sync();
        listener();
      });
      const stopDownloads = downloads.subscribe(() => {
        sync();
        player.sync();
      });
      const stopFrames = queryClient.getQueryCache().subscribe(sync);
      const stopProgress = downloads.onProgress((url) => {
        const state = story.state();
        if (state.kind !== 'video' && url === previewAt(state)) story.mediaProgressed(state.visit);
      });
      return () => {
        stopStory();
        stopDownloads();
        stopFrames();
        stopProgress();
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
      memoryAt,
      preload: createPreload(),
    };
  });
  const state = useSyncExternalStore(subscribe, story.state);
  const arrived = useSyncExternalStore(downloads.subscribe, downloads.snapshot);
  const group = snapshot[state.group] as YearGroup;
  return {
    state,
    group,
    ...storyView(state, group),
    nextVideo: preload(nextVideo(state, memoryAt), arrived.picturesSettled),
    source: (url: string | null) => url && arrived.sourceOf(url),
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
