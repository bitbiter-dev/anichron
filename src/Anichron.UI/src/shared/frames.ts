const swipePixels = 60;
const flickPixelsPerSecond = 800;

export interface Point {
  x: number;
  y: number;
}

export type Swipe = 'left' | 'right' | 'down';

export function swipeOf({ x, y }: Point, velocity: Point, height: number): Swipe | undefined {
  if (Math.abs(x) > Math.abs(y)) {
    if (x <= -swipePixels) return 'left';
    return x >= swipePixels ? 'right' : undefined;
  }
  const flicked = y >= swipePixels && velocity.y >= flickPixelsPerSecond;
  return flicked || y >= height / 3 ? 'down' : undefined;
}

export function createTapGuard() {
  let dragging: boolean | undefined;
  return {
    pressStart: () => {
      dragging = false;
    },
    dragStart: () => {
      dragging = true;
    },
    tapped: () => !dragging,
  };
}

export function clampFrame(index: number, count: number) {
  return Math.max(0, Math.min(index, count - 1));
}

export function createFrameScroll(start: number) {
  let list: HTMLElement | null = null;
  return {
    ...createTapGuard(),
    attach: (element: HTMLElement | null) => {
      list = element;
      if (element) element.scrollLeft = start * element.clientWidth;
    },
    scrollTo: (index: number) => {
      const element = list as HTMLElement;
      element.scrollLeft = index * element.clientWidth;
    },
  };
}
