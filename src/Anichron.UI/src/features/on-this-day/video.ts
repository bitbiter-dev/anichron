import type { Memory } from '../../api/flashbacks';
import type { Downloads, Renewal } from '../../api/media';
import { memoryKind } from './memory-kinds';
import type { Story, StoryState } from './story';

type StoryMedia = Pick<
  Story,
  'state' | 'mediaReady' | 'mediaEnded' | 'mediaFailed' | 'livePlaying' | 'liveEnded'
>;

type InMemory = Pick<Downloads, 'sourceOf' | 'failed'>;

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
  downloads: InMemory,
): Source | null {
  const video = memoryKind(kind).video;
  const url = video?.source(memory, load);
  if (!video || !url) return null;
  const poster = downloads.sourceOf(memory.proxies.preview) ?? '';
  if (!video.live) return { src: url, poster, live: false };
  const src = downloads.sourceOf(url) ?? (downloads.failed(url) ? url : null);
  return play.state !== 'still' && src ? { src, poster, live: true } : null;
}

export function createVideoPlayer(
  story: StoryMedia,
  memoryOf: (state: StoryState) => Memory,
  renewal: () => Renewal,
  downloads: InMemory,
) {
  let element: HTMLVideoElement | null = null;
  let visit = 0;
  let playId = 0;
  let events: MediaEvents;
  let source: Source;
  let lapse: Renewal;
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
      lapse = renewal();
      load(element);
    }
    element.poster = wanted.poster;
    playOrPause(element);
  }

  function failed() {
    const failing = visit;
    void lapse(source.src).then((renewed) => {
      if (!renewed) story.mediaFailed(failing);
      else if (element && story.state().visit === failing) {
        load(element);
        playOrPause(element);
      }
    });
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
