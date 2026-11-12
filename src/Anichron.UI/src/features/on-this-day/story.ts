export const memoryMilliseconds = 5000;

export interface StoryPosition {
  group: number;
  memory: number;
  visit: number;
  ready: boolean;
}

export type Move = 'next' | 'previous' | 'nextGroup' | 'previousGroup';

export type Story = Record<Move | 'close', () => void> & {
  position: () => StoryPosition;
  subscribe: (listener: () => void) => () => void;
  mediaReady: (visit: number) => void;
};

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
  let visits = 0;
  const at = (group: number, memory: number) => ({
    group,
    memory,
    // Stryker disable next-line UpdateOperator
    visit: ++visits,
    ready: !memories(group)[memory],
  });
  let position: StoryPosition = at(group, 0);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let notify: () => void = () => undefined;

  function wait() {
    if (position.ready) timer = setTimeout(next, memoryMilliseconds);
  }

  function show(next: StoryPosition) {
    clearTimeout(timer);
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

  function previous() {
    const { group, memory } = position;
    if (memory > 0) show(at(group, memory - 1));
    else if (group > 0) show(at(group - 1, memories(group - 1).length - 1));
    else show(at(group, 0));
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
    mediaReady(visit) {
      if (visit === position.visit) show({ ...position, ready: true });
    },
    next,
    previous,
    nextGroup() {
      const { group } = position;
      if (group + 1 < pictures.length) show(at(group + 1, 0));
      else close();
    },
    close,
    previousGroup() {
      show(at(Math.max(0, position.group - 1), 0));
    },
  };
}
