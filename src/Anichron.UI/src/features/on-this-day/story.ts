export const memoryMilliseconds = 5000;

export interface StoryPosition {
  group: number;
  memory: number;
  ready: boolean;
}

export interface Story {
  position: () => StoryPosition;
  subscribe: (listener: () => void) => () => void;
  mediaReady: () => void;
}

export type SegmentState = 'seen' | 'playing' | 'next';

export function segmentState(index: number, current: number): SegmentState {
  if (index < current) return 'seen';
  return index === current ? 'playing' : 'next';
}

export function createStory(
  pictures: readonly (readonly boolean[])[],
  group: number,
  close: () => void,
): Story {
  const memories = (group: number) => pictures[group] as readonly boolean[];
  const at = (group: number, memory: number) => ({
    group,
    memory,
    ready: !memories(group)[memory],
  });
  let position: StoryPosition = at(group, 0);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let notify: () => void = () => undefined;

  function wait() {
    if (position.ready) timer = setTimeout(next, memoryMilliseconds);
  }

  function show(next: StoryPosition) {
    position = next;
    notify();
    wait();
  }

  function next() {
    const { group, memory } = position;
    if (memory + 1 < memories(group).length) show(at(group, memory + 1));
    else if (group + 1 < pictures.length) show(at(group + 1, 0));
    else close();
  }

  return {
    position: () => position,
    subscribe(listener) {
      notify = listener;
      wait();
      return () => {
        clearTimeout(timer);
      };
    },
    mediaReady() {
      show({ ...position, ready: true });
    },
  };
}
