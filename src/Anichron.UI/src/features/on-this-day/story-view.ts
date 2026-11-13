import type { Memory, YearGroup } from '../../api/flashbacks';
import { blurhashSize } from './blurhash';
import type { StoryState } from './story';

const kinds: Partial<Record<string, 'story.video' | 'story.livePhoto'>> = {
  Video: 'story.video',
  LivePhoto: 'story.livePhoto',
};

export function storyView(state: StoryState, group: YearGroup) {
  const memory = group.memories[state.memory] as Memory;
  return {
    memory,
    name: {
      kind: kinds[memory.mediaType] ?? 'story.photo',
      year: group.year,
      place: state.memory + 1,
      count: group.memories.length,
    },
    pause: state.paused ? 'story.play' : 'story.pause',
    controls: state.held ? 'hidden' : 'visible',
    picture: state.load !== 'failed',
    blurhash: state.load === 'shown' ? null : memory.blurhash,
    blurhashSize: blurhashSize(memory.width, memory.height),
    failed: state.load === 'failed',
  } as const;
}
