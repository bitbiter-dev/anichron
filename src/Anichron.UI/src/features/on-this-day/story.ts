import { type Kind, type Load, memoryKind } from './memory-kinds';

const memoryMilliseconds = 5000;
const loadMilliseconds = 10_000;
const videoMilliseconds = 120_000;
const hintMilliseconds = 1000;
const upcomingCount = 2;

type PlayedBy = 'hold' | 'button';

export type LivePlay =
  { state: 'still'; by?: undefined } | { state: 'loading' | 'playing'; by: PlayedBy };

export interface StoryMemory {
  kind: Kind;
  durationSeconds: number | null;
}

interface KindInStory {
  length: (load: Load, durationSeconds: number | null) => number;
  timeLimit: (load: Load) => number;
  middle: 'sound' | 'details' | 'next';
  holdPlays: boolean;
  hint: boolean;
  loadsPreview: boolean;
  warmsVideo: boolean;
  downloadsVideo: boolean;
}

export function kindInStory(kind: Kind): KindInStory {
  const picture: KindInStory = {
    length: () => memoryMilliseconds,
    timeLimit: () => memoryMilliseconds,
    middle: 'next',
    holdPlays: false,
    hint: false,
    loadsPreview: true,
    warmsVideo: false,
    downloadsVideo: false,
  };

  const kinds: Record<Kind, KindInStory> = {
    unready: picture,
    picture,
    video: {
      ...picture,
      length: (load, durationSeconds) =>
        load === 'failed'
          ? memoryMilliseconds
          : Math.min((durationSeconds ?? Infinity) * 1000, videoMilliseconds),
      timeLimit: (load) => (load === 'failed' ? memoryMilliseconds : videoMilliseconds),
      middle: 'sound',
      hint: true,
      loadsPreview: false,
      warmsVideo: true,
    },
    live: { ...picture, holdPlays: true, downloadsVideo: true },
    burst: { ...picture, middle: 'details' },
  };
  return kinds[kind];
}

function still(): LivePlay {
  return { state: 'still' };
}

export interface StoryPosition {
  group: number;
  memory: number;
  visit: number;
  kind: Kind;
  load: Load;
  shown: readonly number[];
  playId: number;
  play: LivePlay;
}

export type Place = Pick<StoryPosition, 'group' | 'memory'>;

export interface StoryState extends StoryPosition {
  paused: boolean;
  held: boolean;
  details: boolean;
  ended: boolean;
  running: boolean;
  muted: boolean;
  hint: boolean;
  playsMedia: boolean;
  length: number;
  upcoming: readonly Place[];
}

export type Move = 'next' | 'previous' | 'nextGroup' | 'previousGroup';

export type Tap = Move | 'middle';

export type Story = Record<
  Tap | 'close' | 'closeDetails' | 'togglePause' | 'toggleSound' | 'hold' | 'release' | 'replay',
  () => void
> & {
  state: () => StoryState;
  subscribe: (listener: () => void) => () => void;
  mediaReady: (visit: number) => void;
  mediaProgressed: () => void;
  mediaEnded: (visit: number) => void;
  mediaFailed: (visit: number) => void;
  livePlaying: () => void;
  liveEnded: () => void;
  hide: (place?: Place) => Place;
  undo: (place: Place) => void;
};

export type SegmentState = 'seen' | 'playing' | 'next';

export function segmentState(index: number, current: number, ended: boolean): SegmentState {
  if (index < current || ended) return 'seen';
  return index === current ? 'playing' : 'next';
}

function timeLimitOf({ kind, load }: StoryPosition) {
  return load === 'loading' ? loadMilliseconds : kindInStory(kind).timeLimit(load);
}

function createClock(end: () => void) {
  let remaining = 0;
  let startedAt = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  return {
    set(milliseconds: number) {
      remaining = milliseconds;
    },
    stop() {
      if (timer === undefined) return;
      clearTimeout(timer);
      timer = undefined;
      remaining -= Date.now() - startedAt;
    },
    start() {
      if (timer !== undefined) return;
      startedAt = Date.now();
      timer = setTimeout(end, remaining);
    },
  };
}

export function createStory(
  kinds: readonly (readonly StoryMemory[])[],
  group: number,
  close: () => void,
  decoded: (place: Place) => boolean,
): Story {
  const memories = (group: number) => kinds[group] as readonly StoryMemory[];
  const hidden = new Set<string>();
  const keyOf = ({ group, memory }: Place) => `${String(group)}/${String(memory)}`;
  const shown = (group: number) =>
    [...memories(group).keys()].filter((memory) => !hidden.has(keyOf({ group, memory })));
  const groupsShown = () => [...kinds.keys()].filter((group) => shown(group).length > 0);
  let visits = 0;
  const at = (group: number, memory: number): StoryPosition => {
    const { kind } = memories(group)[memory] as StoryMemory;
    // Stryker disable next-line UpdateOperator
    const visit = ++visits;
    return {
      group,
      memory,
      visit,
      kind,
      load: memoryKind(kind).firstLoad(() => decoded({ group, memory })),
      shown: shown(group),
      playId: visit,
      play: still(),
    };
  };
  const first = (group: number) => at(group, shown(group)[0] as number);
  const places = () =>
    groupsShown().flatMap((group) => shown(group).map((memory) => ({ group, memory })));
  const isAfter = (place: Place, { group, memory }: Place) =>
    place.group > group || (place.group === group && place.memory > memory);
  const upcoming = (from: Place) =>
    places()
      .filter((place) => isAfter(place, from))
      .slice(0, upcomingCount);
  let position: StoryPosition = at(group, 0);
  let paused = false;
  let held = false;
  let details = false;
  let ended = false;
  let muted = true;
  let hinted = kindInStory(position.kind).hint;
  let hint = hinted;
  let state = stateOf();
  const clock = createClock(expire);
  const hintClock = createClock(() => {
    update(() => {
      hint = false;
    });
  });
  clock.set(timeLimitOf(position));
  hintClock.set(hintMilliseconds);
  let notify: (() => void) | undefined;

  function playingLive() {
    return position.play.state === 'playing';
  }

  function playsBy(by: PlayedBy) {
    return position.play.by === by;
  }

  function stateOf(): StoryState {
    const running =
      position.load !== 'loading' && !paused && !held && !details && !ended && !playingLive();
    const playsMedia = held ? playsBy('hold') : !paused && (playsBy('button') || !ended);
    const { group, memory, kind, load } = position;
    const { durationSeconds } = memories(group)[memory] as StoryMemory;
    return {
      ...position,
      paused,
      held,
      details,
      ended,
      running,
      muted,
      hint,
      playsMedia,
      length: kindInStory(kind).length(load, durationSeconds),
      upcoming: upcoming(position),
    };
  }

  function stop() {
    clock.stop();
    hintClock.stop();
  }

  function start() {
    if (!notify || paused || held || details || ended || playingLive()) return;
    clock.start();
    if (hint && position.load !== 'loading') hintClock.start();
  }

  function expire() {
    if (position.load === 'loading') settle(position.visit, 'failed');
    else next(rest);
  }

  function update(change: () => void) {
    stop();
    change();
    state = stateOf();
    notify?.();
    start();
  }

  function show(to: StoryPosition) {
    update(() => {
      position = to;
      ended = false;
      clock.set(timeLimitOf(to));
      hint = kindInStory(to.kind).hint && !hinted;
      hinted ||= hint;
    });
  }

  function settle(visit: number, load: Load, from: readonly Load[] = ['loading']) {
    if (visit === position.visit && from.includes(position.load))
      update(() => {
        position = { ...position, load };
        clock.set(timeLimitOf(position));
      });
  }

  function play(by: PlayedBy) {
    if (kindInStory(position.kind).holdPlays && position.load === 'shown')
      position = { ...position, play: { state: 'loading', by }, playId: ++visits };
  }

  function toggleSound() {
    if (memoryKind(position.kind).sound)
      update(() => {
        muted = !muted;
        hint = false;
      });
  }

  function setDetails(open: boolean) {
    update(() => {
      details = open;
    });
  }

  function rest() {
    update(() => {
      ended = true;
      position = { ...position, play: still() };
    });
  }

  function nextGroup(atEnd = close) {
    const later = groupsShown().find((group) => group > position.group);
    if (later === undefined) atEnd();
    else show(first(later));
  }

  function next(atEnd = close) {
    const { group, memory } = position;
    const later = shown(group).find((index) => index > memory);
    if (later === undefined) nextGroup(atEnd);
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

  const middles = {
    sound: toggleSound,
    details: () => {
      setDetails(true);
    },
    next: () => {
      next();
    },
  };

  return {
    state: () => state,
    subscribe(listener) {
      notify = listener;
      start();
      return () => {
        notify = undefined;
        stop();
      };
    },
    mediaReady(visit) {
      settle(visit, 'shown');
    },
    mediaProgressed() {
      if (position.load !== 'loading' || !kindInStory(position.kind).loadsPreview) return;
      stop();
      clock.set(loadMilliseconds);
      start();
    },
    mediaEnded(visit) {
      if (visit === position.visit) next(rest);
    },
    mediaFailed(visit) {
      settle(visit, 'failed', ['loading', 'shown']);
    },
    livePlaying() {
      const { play } = position;
      if (play.state !== 'still')
        update(() => {
          position = { ...position, play: { ...play, state: 'playing' } };
        });
    },
    liveEnded() {
      update(() => {
        position = { ...position, play: still() };
      });
    },
    replay() {
      update(() => {
        paused = false;
        play('button');
      });
    },
    togglePause() {
      update(() => {
        paused = !paused;
      });
    },
    hold() {
      update(() => {
        held = true;
        play('hold');
      });
    },
    release() {
      update(() => {
        held = false;
        if (playsBy('hold')) position = { ...position, play: still() };
      });
    },
    toggleSound,
    middle() {
      middles[kindInStory(position.kind).middle]();
    },
    closeDetails() {
      setDetails(false);
    },
    next() {
      next();
    },
    previous,
    nextGroup() {
      nextGroup();
    },
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
