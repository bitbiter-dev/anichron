import { type PointerEvent, useEffect, useRef } from 'react';
import { listen } from '../../shared/listen';
import { reducesMotion } from '../../shared/reduced-motion';

const edgePixels = 80;
const glidePixels = 12;
const glideMilliseconds = 16;
const jumpShare = 0.5;
const jumpMilliseconds = 400;

export interface DragTarget<Base> {
  begin: (index: number) => Base;
  hold: (index: number) => Base;
  paint: (base: Base, from: number, to: number) => void;
}

interface Drag<Base> {
  base: Base;
  from: number;
  last: number;
  x: number;
  y: number;
  stop: () => void;
  scrolling?: ReturnType<typeof setInterval>;
}

export function edgeDirection(y: number, height: number) {
  if (y < edgePixels) return -1;
  return y > height - edgePixels ? 1 : 0;
}

export function autoScroll(direction: number, reduced: boolean, height: number) {
  return reduced
    ? { pixels: direction * Math.round(height * jumpShare), every: jumpMilliseconds }
    : { pixels: direction * glidePixels, every: glideMilliseconds };
}

function rowAt(x: number, y: number) {
  const row = document.elementFromPoint(x, y)?.closest<HTMLElement>('[data-index]');
  return row ? Number(row.dataset.index) : null;
}

export function useDragSelect<Base>(target: DragTarget<Base>) {
  const latest = useRef(target);
  const drag = useRef<Drag<Base> | null>(null);
  const claimed = useRef<boolean | undefined>(undefined);

  useEffect(() => {
    latest.current = target;
  });

  function follow(current: Drag<Base>) {
    const index = rowAt(current.x, current.y);
    if (index === null) return;
    // Stryker disable next-line ConditionalExpression
    if (index === current.last) return;
    current.last = index;
    claimed.current = true;
    latest.current.paint(current.base, current.from, index);
  }

  function steer(current: Drag<Base>) {
    clearInterval(current.scrolling);
    const direction = edgeDirection(current.y, innerHeight);
    if (direction === 0) return;
    const { pixels, every } = autoScroll(direction, reducesMotion(), innerHeight);
    current.scrolling = setInterval(() => {
      scrollBy(0, pixels);
      follow(current);
    }, every);
  }

  function end() {
    const current = drag.current;
    if (!current) return;
    clearInterval(current.scrolling);
    current.stop();
    drag.current = null;
  }

  function begin(base: Base, index: number, x: number, y: number) {
    end();
    const holdTouch = (event: Event) => {
      event.preventDefault();
    };
    const current: Drag<Base> = { base, from: index, last: index, x, y, stop: () => undefined };
    const stops = [
      listen(window, 'pointermove', (event) => {
        ({ clientX: current.x, clientY: current.y } = event as globalThis.PointerEvent);
        follow(current);
        steer(current);
      }),
      listen(window, 'pointerup', end),
      listen(window, 'pointercancel', end),
    ];
    // Stryker disable next-line ObjectLiteral
    window.addEventListener('touchmove', holdTouch, { passive: false });
    current.stop = () => {
      for (const stop of stops) stop();
      window.removeEventListener('touchmove', holdTouch);
    };
    drag.current = current;
  }

  // Stryker disable next-line ArrayDeclaration
  useEffect(() => end, []);

  return {
    handle: (index: number) => (event: PointerEvent) => {
      if (event.button !== 0) return;
      begin(latest.current.begin(index), index, event.clientX, event.clientY);
    },
    hold: (index: number) => {
      begin(latest.current.hold(index), index, NaN, NaN);
    },
    pressStart: () => {
      claimed.current = false;
    },
    claimed: () => claimed.current,
  };
}
