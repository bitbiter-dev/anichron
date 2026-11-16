import type { Memory, YearGroup } from '../../api/flashbacks';
import { blurhashOf } from './blurhash';
import { type Kind, memoryMilliseconds, type StoryState, videoMilliseconds } from './story';

export function kindOf({ proxies, livePhotoVideoUrl, burst }: Memory): Kind {
  if (proxies.video) return 'video';
  if (!proxies.preview) return 'none';
  if (burst) return 'burst';
  return livePhotoVideoUrl ? 'live' : 'picture';
}

function durationOf({ kind, load }: StoryState, { durationSeconds }: Memory) {
  if (kind !== 'video' || load === 'failed') return memoryMilliseconds;
  return Math.min((durationSeconds ?? Infinity) * 1000, videoMilliseconds);
}

function imageOf({ kind, load }: StoryState) {
  if (kind === 'video') return load === 'failed' ? 'poster' : null;
  return load === 'failed' ? null : 'picture';
}

export type LiveState = 'loading' | 'playing' | 'still';

function liveOf({ livePlay, liveShown, playsMedia }: StoryState): LiveState {
  if (!livePlay || !playsMedia) return 'still';
  return liveShown ? 'playing' : 'loading';
}

export function storyView(state: StoryState, group: YearGroup) {
  const kinds: Partial<Record<string, 'story.video' | 'story.livePhoto'>> = {
    Video: 'story.video',
    LivePhoto: 'story.livePhoto',
  };
  const burst = state.kind === 'burst' ? 'story.burst' : undefined;
  const memory = group.memories[state.memory] as Memory;
  const segments = { count: state.shown.length, current: state.shown.indexOf(state.memory) };
  return {
    memory,
    segments,
    name: {
      kind: burst ?? kinds[memory.mediaType] ?? 'story.photo',
      year: group.year,
      place: segments.current + 1,
      count: segments.count,
    },
    pause: state.paused ? 'story.play' : 'story.pause',
    controls: state.held ? 'hidden' : 'visible',
    blurhash: state.load === 'shown' ? null : blurhashOf(memory),
    failed: state.kind !== 'video' && state.load === 'failed',
    image: imageOf(state),
    video: (state.kind === 'video' && state.load !== 'failed') || state.liveShown,
    liveButton: state.kind === 'live' && state.load === 'shown',
    live: liveOf(state),
    duration: durationOf(state, memory),
    sound: {
      shown: state.kind === 'video',
      hint: state.hint,
      label: state.muted ? 'story.soundOn' : 'story.soundOff',
    },
  } as const;
}
