import { createTapGuard, type Point, type Swipe, swipeOf } from '../../shared/frames';
import { listen } from '../../shared/listen';
import type { Move, Story, Tap } from './story';

type KeyAction = Tap | 'togglePause' | 'toggleSound';

function keyAction(event: KeyboardEvent): KeyAction | undefined {
  if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return undefined;
  const keys: Partial<Record<string, readonly [KeyAction, KeyAction | undefined]>> = {
    ArrowLeft: ['previous', 'previousGroup'],
    ArrowRight: ['next', 'nextGroup'],
    ' ': ['togglePause', undefined],
    Enter: ['middle', undefined],
    m: ['toggleSound', undefined],
    M: ['toggleSound', 'toggleSound'],
  };
  return keys[event.key]?.[Number(event.shiftKey)];
}

export function listenToInput(story: Story) {
  const onKey = (event: Event) => {
    const action = keyAction(event as KeyboardEvent);
    if (action && !story.state().details) story[action]();
  };
  const stops = [
    listen(document, 'keydown', onKey),
    ...['pointerup', 'pointercancel'].map((type) => listen(window, type, story.release)),
  ];
  return () => {
    for (const stop of stops) stop();
  };
}

function swipeMove(offset: Point, velocity: Point, height: number) {
  const moves: Record<Swipe, Move | 'close'> = {
    left: 'nextGroup',
    right: 'previousGroup',
    down: 'close',
  };
  const swipe = swipeOf(offset, velocity, height);
  return swipe && moves[swipe];
}

export function createGestures(story: Story) {
  const guard = createTapGuard();
  return {
    hold: story.hold,
    pressStart: guard.pressStart,
    dragStart: guard.dragStart,
    tap: (move: Tap) => () => {
      if (guard.tapped()) story[move]();
    },
    dragEnd: (offset: Point, velocity: Point, height: number) => {
      const move = swipeMove(offset, velocity, height);
      if (move) story[move]();
    },
  };
}

export type Gestures = ReturnType<typeof createGestures>;
