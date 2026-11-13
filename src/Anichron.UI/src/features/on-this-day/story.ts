export const memoryMilliseconds = 5000;
const loadMilliseconds = 10_000;
export const videoMilliseconds = 60_000;
const hintMilliseconds = 2000;

export type Load = 'loading' | 'shown' | 'failed';

export type Kind = 'none' | 'picture' | 'video';

export interface StoryPosition {
  group: number;
  memory: number;
  visit: number;
  kind: Kind;
  load: Load;
  shown: readonly number[];
}

export type Place = Pick<StoryPosition, 'group' | 'memory'>;

export interface StoryState extends StoryPosition {
  paused: boolean;
  held: boolean;
  running: boolean;
  muted: boolean;
  hint: boolean;
}

export type Move = 'next' | 'previous' | 'nextGroup' | 'previousGroup';

export type Tap = Move | 'middle';

export type Story = Record<
  Tap | 'close' | 'togglePause' | 'toggleSound' | 'hold' | 'release',
  () => void
> & {
  state: () => StoryState;
  subscribe: (listener: () => void) => () => void;
  mediaReady: (visit: number) => void;
  mediaEnded: (visit: number) => void;
  mediaFailed: (visit: number) => void;
  hide: (place?: Place) => Place;
  undo: (place: Place) => void;
};

export type SegmentState = 'seen' | 'playing' | 'next';

export function segmentState(index: number, current: number): SegmentState {
  if (index < current) return 'seen';
  return index === current ? 'playing' : 'next';
}

function lasting({ kind, load }: StoryPosition) {
  if (load === 'loading') return loadMilliseconds;
  return kind === 'video' && load === 'shown' ? videoMilliseconds : memoryMilliseconds;
}

export function createStory(
  kinds: readonly (readonly Kind[])[],
  group: number,
  close: () => void,
): Story {
  const memories = (group: number) => kinds[group] as readonly Kind[];
  const hidden = new Set<string>();
  const keyOf = ({ group, memory }: Place) => `${String(group)}/${String(memory)}`;
  const shown = (group: number) =>
    [...memories(group).keys()].filter((memory) => !hidden.has(keyOf({ group, memory })));
  const groupsShown = () => [...kinds.keys()].filter((group) => shown(group).length > 0);
  let visits = 0;
  const at = (group: number, memory: number): StoryPosition => {
    const kind = memories(group)[memory] as Kind;
    return {
      group,
      memory,
      // Stryker disable next-line UpdateOperator
      visit: ++visits,
      kind,
      load: kind === 'none' ? 'shown' : 'loading',
      shown: shown(group),
    };
  };
  const first = (group: number) => at(group, shown(group)[0] as number);
  let position: StoryPosition = at(group, 0);
  let paused = false;
  let held = false;
  let muted = true;
  let hinted = position.kind === 'video';
  let hint = hinted;
  let state = stateOf();
  let remaining = lasting(position);
  let startedAt = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let hintTimer: ReturnType<typeof setTimeout> | undefined;
  let notify: (() => void) | undefined;

  function stateOf(): StoryState {
    const running = position.load !== 'loading' && !paused && !held;
    return { ...position, paused, held, running, muted, hint };
  }

  function stop() {
    if (timer === undefined) return;
    clearTimeout(timer);
    timer = undefined;
    remaining -= Date.now() - startedAt;
  }

  function start() {
    if (!notify || paused || held) return;
    startedAt = Date.now();
    timer = setTimeout(expire, remaining);
  }

  function expire() {
    if (position.load === 'loading') settle(position.visit, 'failed');
    else next();
  }

  function update(change: () => void) {
    stop();
    change();
    state = stateOf();
    notify?.();
    start();
  }

  function endHintLater() {
    clearTimeout(hintTimer);
    if (hint && notify)
      hintTimer = setTimeout(() => {
        update(() => {
          hint = false;
        });
      }, hintMilliseconds);
  }

  function show(to: StoryPosition) {
    update(() => {
      position = to;
      remaining = lasting(to);
      hint = to.kind === 'video' && !hinted;
      hinted ||= hint;
    });
    endHintLater();
  }

  function settle(visit: number, load: Load) {
    if (visit === position.visit && position.load === 'loading')
      update(() => {
        position = { ...position, load };
        remaining = lasting(position);
      });
  }

  function toggleSound() {
    if (position.kind === 'video')
      update(() => {
        muted = !muted;
        hint = false;
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
      endHintLater();
      return () => {
        notify = undefined;
        stop();
        clearTimeout(hintTimer);
      };
    },
    mediaReady(visit) {
      settle(visit, 'shown');
    },
    mediaEnded(visit) {
      if (visit === position.visit) next();
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
    toggleSound,
    middle() {
      if (position.kind === 'video') toggleSound();
      else next();
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
