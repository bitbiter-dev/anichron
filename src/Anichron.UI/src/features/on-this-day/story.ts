export const memoryMilliseconds = 5000;

export interface StoryPosition {
  group: number;
  memory: number;
  visit: number;
  ready: boolean;
  shown: readonly number[];
}

export type Place = Pick<StoryPosition, 'group' | 'memory'>;

export type Move = 'next' | 'previous' | 'nextGroup' | 'previousGroup';

export type Story = Record<Move | 'close', () => void> & {
  position: () => StoryPosition;
  subscribe: (listener: () => void) => () => void;
  mediaReady: (visit: number) => void;
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
  const at = (group: number, memory: number) => ({
    group,
    memory,
    // Stryker disable next-line UpdateOperator
    visit: ++visits,
    ready: !memories(group)[memory],
    shown: shown(group),
  });
  const first = (group: number) => at(group, shown(group)[0] as number);
  let position: StoryPosition = at(group, 0);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let notify: (() => void) | undefined;

  function wait() {
    if (notify && position.ready) timer = setTimeout(next, memoryMilliseconds);
  }

  function show(next: StoryPosition) {
    clearTimeout(timer);
    position = next;
    notify?.();
    wait();
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
    position: () => position,
    subscribe(listener) {
      notify = listener;
      wait();
      return () => {
        notify = undefined;
        clearTimeout(timer);
      };
    },
    mediaReady(visit) {
      if (visit === position.visit) show({ ...position, ready: true });
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
      else {
        position = { ...position, shown: shown(position.group) };
        notify?.();
      }
      return place;
    },
    undo(place) {
      hidden.delete(keyOf(place));
      show(at(place.group, place.memory));
    },
  };
}
