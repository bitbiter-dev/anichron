export const memoryMilliseconds = 5000;

export interface StoryPosition {
  group: number;
  memory: number;
  ready: boolean;
}

export interface Story {
  position: () => StoryPosition;
  subscribe: (listener: () => void) => () => void;
  mediaReady: () => void;
}

export function createStory(sizes: readonly number[], group: number, close: () => void): Story {
  let position: StoryPosition = { group, memory: 0, ready: false };
  let timer: ReturnType<typeof setTimeout> | undefined;
  let notify: () => void = () => undefined;

  function show(next: StoryPosition) {
    position = next;
    notify();
  }

  function next() {
    const { group, memory } = position;
    if (memory + 1 < (sizes[group] ?? 0)) show({ group, memory: memory + 1, ready: false });
    else if (group + 1 < sizes.length) show({ group: group + 1, memory: 0, ready: false });
    else close();
  }

  return {
    position: () => position,
    subscribe(listener) {
      notify = listener;
      return () => {
        clearTimeout(timer);
      };
    },
    mediaReady() {
      show({ ...position, ready: true });
      timer = setTimeout(next, memoryMilliseconds);
    },
  };
}
