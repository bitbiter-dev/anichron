import type { Memory, YearGroup } from '../../api/flashbacks';
import { blurhashOf } from './blurhash';
import type { StoryState } from './story';

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
    failed: state.load === 'failed',
  } as const;
}
