import type { Memory, YearGroup } from '../../api/flashbacks';
import { blurhashOf } from './blurhash';
import { type Kind, memoryMilliseconds, type StoryState } from './story';

const longestVideoSeconds = 60;

export function kindOf({ mediaType, proxies }: Memory): Kind {
  if (mediaType === 'Video' && proxies.video) return 'video';
  return proxies.preview ? 'picture' : 'none';
}

function durationOf({ kind, load }: StoryState, { durationSeconds }: Memory) {
  if (kind !== 'video' || load === 'failed') return memoryMilliseconds;
  return Math.min(durationSeconds ?? longestVideoSeconds, longestVideoSeconds) * 1000;
}

function imageOf({ kind, load }: StoryState) {
  if (kind === 'video') return load === 'failed' ? 'poster' : null;
  return load === 'failed' ? null : 'picture';
}

export function storyView(state: StoryState, group: YearGroup) {
  const kinds: Partial<Record<string, 'story.video' | 'story.livePhoto'>> = {
    Video: 'story.video',
    LivePhoto: 'story.livePhoto',
  };
  const memory = group.memories[state.memory] as Memory;
  const segments = { count: state.shown.length, current: state.shown.indexOf(state.memory) };
  return {
    memory,
    segments,
    name: {
      kind: kinds[memory.mediaType] ?? 'story.photo',
      year: group.year,
      place: segments.current + 1,
      count: segments.count,
    },
    pause: state.paused ? 'story.play' : 'story.pause',
    controls: state.held ? 'hidden' : 'visible',
    blurhash: state.load === 'shown' ? null : blurhashOf(memory),
    failed: state.kind !== 'video' && state.load === 'failed',
    image: imageOf(state),
    video: state.kind === 'video' && state.load !== 'failed',
    duration: durationOf(state, memory),
    sound: {
      shown: state.kind === 'video',
      hint: state.hint,
      label: state.muted ? 'story.soundOn' : 'story.soundOff',
    },
  } as const;
}
