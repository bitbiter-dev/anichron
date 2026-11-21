import type { Memory } from '../../api/flashbacks';
import { recover } from './media';
import { memoryKind } from './memory-kinds';
import type { Story, StoryState } from './story';

type StoryMedia = Pick<
  Story,
  'state' | 'mediaReady' | 'mediaEnded' | 'mediaFailed' | 'livePlaying' | 'liveEnded'
>;

interface Source {
  src: string;
  poster: string;
  live: boolean;
}

interface MediaEvents {
  playing: () => void;
  ended: () => void;
  error: () => void;
}

function sourceOf(
  { kind, load, play }: StoryState,
  memory: Memory,
  liveSource: (url: string) => string,
): Source | null {
  const video = memoryKind(kind).video;
  const url = video?.source(memory, load);
  if (!video || !url || (video.live && play.state === 'still')) return null;
  const { live } = video;
  return { src: live ? liveSource(url) : url, poster: memory.proxies.preview ?? '', live };
}

export function createVideoPlayer(
  story: StoryMedia,
  memoryOf: (state: StoryState) => Memory,
  renew: () => Promise<void>,
  liveSource: (url: string) => string,
) {
  let element: HTMLVideoElement | null = null;
  let visit = 0;
  let playId = 0;
  let events: MediaEvents;
  let source: Source;
  let reloaded: boolean;
  let playing: boolean;

  function load(video: HTMLVideoElement) {
    video.poster = source.poster;
    video.src = source.src;
    playing = false;
  }

  function playOrPause(video: HTMLVideoElement) {
    const wanted = story.state().playsMedia;
    if (wanted === playing) return;
    playing = wanted;
    if (wanted)
      video.play().catch(() => {
        playing = false;
      });
    else video.pause();
  }

  function sync() {
    if (!element) return;
    const state = story.state();
    const wanted = sourceOf(state, memoryOf(state), liveSource);
    element.muted = state.muted || wanted?.live === true;
    if (!wanted) {
      element.pause();
      if (element.hasAttribute('src')) {
        element.removeAttribute('src');
        element.load();
      }
      return;
    }
    if (state.playId !== playId) {
      playId = state.playId;
      visit = state.visit;
      events = wanted.live ? liveEvents : videoEvents;
      source = wanted;
      reloaded = false;
      load(element);
    }
    playOrPause(element);
  }

  function failed() {
    const failing = visit;
    recover(
      reloaded,
      renew,
      () => {
        reloaded = true;
        if (!element || story.state().visit !== failing) return;
        load(element);
        playOrPause(element);
      },
      () => {
        story.mediaFailed(failing);
      },
    );
  }

  const liveEvents: MediaEvents = {
    playing: story.livePlaying,
    ended: story.liveEnded,
    error: story.liveEnded,
  };
  const videoEvents: MediaEvents = {
    playing: () => {
      story.mediaReady(visit);
    },
    ended: () => {
      story.mediaEnded(visit);
    },
    error: failed,
  };

  return {
    sync,
    events: {
      ref: (video: HTMLVideoElement | null) => {
        element = video;
        sync();
      },
      onPlaying: () => {
        events.playing();
      },
      onEnded: () => {
        events.ended();
      },
      onError: () => {
        events.error();
      },
    },
  };
}
