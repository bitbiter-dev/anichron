import type { Memory } from '../../api/flashbacks';
import type { Downloads } from '../../api/media';
import { recover } from './media';
import type { Story, StoryState } from './story';

type StoryMedia = Pick<
  Story,
  'state' | 'mediaReady' | 'mediaEnded' | 'mediaFailed' | 'livePlaying' | 'liveEnded'
>;

type InMemory = Pick<Downloads, 'sourceOf' | 'failed'>;

interface Source {
  src: string;
  poster: string;
}

interface MediaEvents {
  playing: () => void;
  ended: () => void;
  error: () => void;
}

function sourceOf(
  state: StoryState,
  { proxies, livePhotoVideoUrl }: Memory,
  downloads: InMemory,
): Source | null {
  const poster = (proxies.preview && downloads.sourceOf(proxies.preview)) ?? '';
  if (state.kind === 'video' && state.load !== 'failed')
    return { src: proxies.video as string, poster };
  const live = livePhotoVideoUrl as string;
  const src = downloads.sourceOf(live) ?? (downloads.failed(live) ? live : null);
  return state.livePlay && src ? { src, poster } : null;
}

export function createVideoPlayer(
  story: StoryMedia,
  memoryOf: (state: StoryState) => Memory,
  renew: () => Promise<void>,
  downloads: InMemory,
) {
  let element: HTMLVideoElement | null = null;
  let visit = 0;
  let playId = 0;
  let events: MediaEvents;
  let source: Source;
  let reloaded: boolean;
  let playing: boolean;

  function load(video: HTMLVideoElement) {
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
    const wanted = sourceOf(state, memoryOf(state), downloads);
    element.muted = state.muted || state.kind === 'live';
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
      events = state.kind === 'live' ? liveEvents : videoEvents;
      source = wanted;
      reloaded = false;
      load(element);
    }
    element.poster = wanted.poster;
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
