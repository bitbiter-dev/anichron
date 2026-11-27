import type { YearGroup } from '../../api/flashbacks';
import type { Memory } from '../../api/memories';
import { loadView, memoryKind, soundLabel } from '../../shared/memory-kinds';
import type { LivePlay, StoryState } from './story';

export type LiveState = LivePlay['state'];

function liveOf({ play, playsMedia }: StoryState): LiveState {
  return playsMedia ? play.state : 'still';
}

function videoShown({ kind, load, play }: StoryState, memory: Memory) {
  const video = memoryKind(kind).video;
  if (video?.live) return play.state === 'playing';
  return video?.source(memory, load) != null;
}

export function storyView(state: StoryState, group: YearGroup) {
  const kind = memoryKind(state.kind);
  const memory = group.memories[state.memory] as Memory;
  const segments = { count: state.shown.length, current: state.shown.indexOf(state.memory) };
  return {
    memory,
    segments,
    name: {
      kind: kind.name(memory),
      year: group.year,
      place: segments.current + 1,
      count: segments.count,
    },
    pause: state.paused ? 'story.play' : 'story.pause',
    controls: state.held ? 'hidden' : 'visible',
    ...loadView(kind, memory, state.load),
    image: kind.image(state.load),
    video: videoShown(state, memory),
    live: liveOf(state),
    duration: state.length,
    sound: {
      shown: kind.sound,
      hint: state.hint,
      label: soundLabel(state.muted),
    },
  } as const;
}
