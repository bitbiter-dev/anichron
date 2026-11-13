export const memoryMilliseconds = 5000;
const loadMilliseconds = 10_000;

export type Load = 'loading' | 'shown' | 'failed';

export interface StoryPosition {
  group: number;
  memory: number;
  visit: number;
  load: Load;
  shown: readonly number[];
}

export type Place = Pick<StoryPosition, 'group' | 'memory'>;

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
  hide: (place?: Place) => Place;
  undo: (place: Place) => void;
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
  const hidden = new Set<string>();
  const keyOf = ({ group, memory }: Place) => `${String(group)}/${String(memory)}`;
  const shown = (group: number) =>
    [...memories(group).keys()].filter((memory) => !hidden.has(keyOf({ group, memory })));
  const groupsShown = () => [...pictures.keys()].filter((group) => shown(group).length > 0);
  let visits = 0;
  const at = (group: number, memory: number): StoryPosition => ({
    group,
    memory,
    // Stryker disable next-line UpdateOperator
    visit: ++visits,
    load: memories(group)[memory] ? 'loading' : 'shown',
    shown: shown(group),
  });
  const first = (group: number) => at(group, shown(group)[0] as number);
  let position: StoryPosition = at(group, 0);
  let paused = false;
  let held = false;
  let state = stateOf();
  let remaining = memoryMilliseconds;
  let startedAt = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let watchdog: ReturnType<typeof setTimeout> | undefined;
  let notify: (() => void) | undefined;

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
    if (!notify || !state.running) return;
    startedAt = Date.now();
    timer = setTimeout(next, remaining);
  }

  function update(change: () => void) {
    stop();
    change();
    state = stateOf();
    notify?.();
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

  function nextGroup() {
    const later = groupsShown().find((group) => group > position.group);
    if (later === undefined) close();
    else show(first(later));
  }

  function next() {
    const { group, memory } = position;
    const later = shown(group).find((index) => index > memory);
    if (later === undefined) nextGroup();
    else show(at(group, later));
  }

  function previous() {
    const { group, memory } = position;
    const earlier = shown(group).findLast((index) => index < memory);
    const before = groupsShown().findLast((index) => index < group);
    if (earlier !== undefined) show(at(group, earlier));
    else if (before === undefined) show(at(group, memory));
    else show(at(before, shown(before).at(-1) as number));
  }

  return {
    state: () => state,
    subscribe(listener) {
      notify = listener;
      start();
      watch();
      return () => {
        notify = undefined;
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
    nextGroup,
    close,
    previousGroup() {
      const { group } = position;
      show(first(groupsShown().findLast((index) => index < group) ?? group));
    },
    hide(place = position) {
      hidden.add(keyOf(place));
      if (place.group === position.group && place.memory === position.memory) next();
      else
        update(() => {
          position = { ...position, shown: shown(position.group) };
        });
      return place;
    },
    undo(place) {
      hidden.delete(keyOf(place));
      show(at(place.group, place.memory));
    },
  };
}
