export const memoryMilliseconds = 5000;
const loadMilliseconds = 10_000;

export type Load = 'loading' | 'shown' | 'failed';

export interface StoryPosition {
  group: number;
  memory: number;
  visit: number;
  load: Load;
}

export interface StoryState extends StoryPosition {
  paused: boolean;
  held: boolean;
  running: boolean;
}

export type Move = 'next' | 'previous' | 'nextGroup' | 'previousGroup';

export type Story = Record<Move | 'close' | 'togglePause' | 'hold' | 'release', () => void> & {
  state: () => StoryState;
  subscribe: (listener: () => void) => () => void;
  mediaReady: (visit: number) => void;
  mediaFailed: (visit: number) => void;
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
  const at = (group: number, memory: number): StoryPosition => ({
    group,
    memory,
    // Stryker disable next-line UpdateOperator
    visit: ++visits,
    load: memories(group)[memory] ? 'loading' : 'shown',
  });
  let position: StoryPosition = at(group, 0);
  let paused = false;
  let held = false;
  let state = stateOf();
  let remaining = memoryMilliseconds;
  let startedAt = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let watchdog: ReturnType<typeof setTimeout> | undefined;
  let notify: () => void = () => undefined;

  function stateOf(): StoryState {
    return { ...position, paused, held, running: position.load !== 'loading' && !paused && !held };
  }

  function stop() {
    if (timer === undefined) return;
    clearTimeout(timer);
    timer = undefined;
    remaining -= Date.now() - startedAt;
  }

  function start() {
    if (!state.running) return;
    startedAt = Date.now();
    timer = setTimeout(next, remaining);
  }

  function update(change: () => void) {
    stop();
    change();
    state = stateOf();
    notify();
    start();
  }

  function watch() {
    clearTimeout(watchdog);
    watchdog = setTimeout(() => {
      settle(position.visit, 'failed');
    }, loadMilliseconds);
  }

  function show(to: StoryPosition) {
    update(() => {
      position = to;
      remaining = memoryMilliseconds;
    });
    watch();
  }

  function settle(visit: number, load: Load) {
    if (visit === position.visit && position.load === 'loading')
      update(() => {
        position = { ...position, load };
      });
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
    state: () => state,
    subscribe(listener) {
      notify = listener;
      start();
      watch();
      return () => {
        stop();
        clearTimeout(watchdog);
      };
    },
    mediaReady(visit) {
      settle(visit, 'shown');
    },
    mediaFailed(visit) {
      settle(visit, 'failed');
    },
    togglePause() {
      update(() => {
        paused = !paused;
      });
    },
    hold() {
      update(() => {
        held = true;
      });
    },
    release() {
      update(() => {
        held = false;
      });
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
