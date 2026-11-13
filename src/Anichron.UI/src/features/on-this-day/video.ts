import type { Memory } from '../../api/flashbacks';
import { recover } from './media';
import type { Story } from './story';

type StoryMedia = Pick<Story, 'state' | 'mediaReady' | 'mediaEnded' | 'mediaFailed'>;

export function createVideoPlayer(
  story: StoryMedia,
  memoryOf: (state: ReturnType<Story['state']>) => Memory,
  renew: () => Promise<void>,
) {
  let element: HTMLVideoElement | null = null;
  let visit = 0;
  let reloaded: boolean;
  let playing: boolean;

  function load(video: HTMLVideoElement) {
    const { proxies } = memoryOf(story.state());
    video.poster = proxies.preview ?? '';
    video.src = proxies.video as string;
    playing = false;
  }

  function playOrPause(video: HTMLVideoElement) {
    const { paused, held } = story.state();
    const wanted = !paused && !held;
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
    element.muted = state.muted;
    if (state.kind !== 'video' || state.load === 'failed') {
      element.pause();
      if (element.hasAttribute('src')) {
        element.removeAttribute('src');
        element.load();
      }
      return;
    }
    if (state.visit !== visit) {
      visit = state.visit;
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

  return {
    sync,
    events: {
      ref: (video: HTMLVideoElement | null) => {
        element = video;
        sync();
      },
      onPlaying: () => {
        story.mediaReady(visit);
      },
      onEnded: () => {
        story.mediaEnded(visit);
      },
      onError: failed,
    },
  };
}
