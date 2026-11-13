import { listen } from '../../shared/listen';
import type { Move, Story } from './story';

type KeyAction = Move | 'togglePause';

function keyAction(event: KeyboardEvent): KeyAction | undefined {
  if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return undefined;
  const keys: Partial<Record<string, readonly [KeyAction, KeyAction | undefined]>> = {
    ArrowLeft: ['previous', 'previousGroup'],
    ArrowRight: ['next', 'nextGroup'],
    ' ': ['togglePause', undefined],
  };
  return keys[event.key]?.[Number(event.shiftKey)];
}

export function listenToKeys(story: Story) {
  const onKey = (event: KeyboardEvent) => {
    const action = keyAction(event);
    if (action) story[action]();
  };
  document.addEventListener('keydown', onKey);
  return () => {
    document.removeEventListener('keydown', onKey);
  };
}

export function listenToRelease(story: Story) {
  const stops = ['pointerup', 'pointercancel'].map((type) => listen(window, type, story.release));
  return () => {
    for (const stop of stops) stop();
  };
}

const swipePixels = 60;

interface Point {
  x: number;
  y: number;
}

function swipeMove({ x, y }: Point): Move | 'close' | undefined {
  if (Math.abs(x) > Math.abs(y)) {
    if (x <= -swipePixels) return 'nextGroup';
    return x >= swipePixels ? 'previousGroup' : undefined;
  }
  return y >= swipePixels ? 'close' : undefined;
}

export function createGestures(story: Story) {
  let swiping: boolean | undefined;
  return {
    hold: story.hold,
    pressStart: () => {
      swiping = false;
    },
    dragStart: () => {
      swiping = true;
    },
    tap: (move: Move) => () => {
      if (!swiping) story[move]();
    },
    dragEnd: (offset: Point) => {
      const move = swipeMove(offset);
      if (move) story[move]();
    },
  };
}

export type Gestures = ReturnType<typeof createGestures>;
