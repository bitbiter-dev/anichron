import { listen } from '../../shared/listen';
import type { Move, Story, Tap } from './story';

type KeyAction = Move | 'togglePause' | 'toggleSound';

function keyAction(event: KeyboardEvent): KeyAction | undefined {
  if (event.repeat || event.altKey || event.ctrlKey || event.metaKey) return undefined;
  const keys: Partial<Record<string, readonly [KeyAction, KeyAction | undefined]>> = {
    ArrowLeft: ['previous', 'previousGroup'],
    ArrowRight: ['next', 'nextGroup'],
    ' ': ['togglePause', undefined],
    m: ['toggleSound', undefined],
    M: ['toggleSound', 'toggleSound'],
  };
  return keys[event.key]?.[Number(event.shiftKey)];
}

export function listenToInput(story: Story) {
  const onKey = (event: Event) => {
    const action = keyAction(event as KeyboardEvent);
    if (action) story[action]();
  };
  const stops = [
    listen(document, 'keydown', onKey),
    ...['pointerup', 'pointercancel'].map((type) => listen(window, type, story.release)),
  ];
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
    tap: (move: Tap) => () => {
      if (!swiping) story[move]();
    },
    dragEnd: (offset: Point) => {
      const move = swipeMove(offset);
      if (move) story[move]();
    },
  };
}

export type Gestures = ReturnType<typeof createGestures>;
