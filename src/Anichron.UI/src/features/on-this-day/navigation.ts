import type { Move, Story } from './story';

const arrows: Partial<Record<string, readonly [Move, Move]>> = {
  ArrowLeft: ['previous', 'previousGroup'],
  ArrowRight: ['next', 'nextGroup'],
};

export function keyMove(event: KeyboardEvent): Move | undefined {
  if (event.altKey || event.ctrlKey || event.metaKey) return undefined;
  return arrows[event.key]?.[Number(event.shiftKey)];
}

export const swipePixels = 60;

interface Point {
  x: number;
  y: number;
}

export function swipeMove({ x, y }: Point): Move | 'close' | undefined {
  if (Math.abs(x) > Math.abs(y)) {
    if (x <= -swipePixels) return 'nextGroup';
    return x >= swipePixels ? 'previousGroup' : undefined;
  }
  return y >= swipePixels ? 'close' : undefined;
}

export function createGestures(story: Story) {
  let swiping = false;
  return {
    pointerDown: () => {
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
