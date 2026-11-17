import { useRouter } from '@tanstack/react-router';
import { useState, useSyncExternalStore } from 'react';
import type { Memory, YearGroup } from '../../api/flashbacks';
import { cardOf } from './burst';
import { burstQuery, startGroup } from './day';
import { createGestures, listenToInput } from './navigation';
import { createKeepLiveVideos, createPrefetch, nextVideo } from './prefetch';
import { createStory, type Place } from './story';
import { kindOf, storyView } from './story-view';
import { createVideoPlayer } from './video';

export function useStory(years: readonly YearGroup[], year: number, close: () => void) {
  const { api, queryClient } = useRouter().options.context;
  const { session, media } = api;
  const [{ snapshot, story, gestures, player, subscribe, memoryAt }] = useState(() => {
    const memoryAt = ({ group, memory }: Place) =>
      (years[group] as YearGroup).memories[memory] as Memory;
    const prefetch = createPrefetch(memoryAt, (url) => media.loadPicture(url));
    const previewAt = (place: Place) => {
      const memory = memoryAt(place);
      const frames = queryClient.getQueryData(
        burstQuery(api, String(memory.burst?.burstId)).queryKey,
      );
      return cardOf(memory, frames).proxies.preview;
    };
    const story = createStory(
      years.map((group) => group.memories.map(kindOf)),
      startGroup(years, year),
      close,
      (place) => prefetch.decoded(previewAt(place)),
    );
    const videos = media.videos();
    const player = createVideoPlayer(
      story,
      memoryAt,
      () => session.renewMedia(),
      (url) => videos.sourceOf(url),
    );
    const keepLiveVideos = createKeepLiveVideos(memoryAt, (urls) => {
      videos.keep(urls);
    });
    const subscribe = (listener: () => void) => {
      const stopInput = listenToInput(story);
      prefetch.fetchAhead(story.state());
      const stop = story.subscribe(() => {
        keepLiveVideos(story.state());
        player.sync();
        prefetch.fetchAhead(story.state());
        listener();
      });
      return () => {
        stop();
        stopInput();
        videos.clear();
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
    closeDetails: story.closeDetails,
    togglePause: story.togglePause,
    toggleSound: story.toggleSound,
    replay: story.replay,
  };
}
