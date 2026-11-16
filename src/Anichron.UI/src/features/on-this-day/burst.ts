import type { Memory } from '../../api/flashbacks';

type Burst = NonNullable<Memory['burst']>;

export function burstIdOf({ burst }: Memory) {
  return (burst as Burst).burstId;
}

export function otherFrames({ burst }: Memory) {
  return (burst?.count ?? 1) - 1;
}

export function framesShown(frames: readonly Memory[] | undefined) {
  return frames?.filter(({ interaction }) => !interaction.hidden);
}

export function cardOf(memory: Memory, frames: readonly Memory[] | undefined): Memory {
  const shown = framesShown(frames) ?? [];
  const cover = shown[0];
  if (!cover) return memory;
  return { ...cover, burst: { ...(cover.burst as Burst), count: shown.length } };
}

export function clampFrame(index: number, count: number) {
  return Math.max(0, Math.min(index, count - 1));
}

export function createFrameScroll() {
  let list: HTMLElement | null = null;
  let dragging: boolean | undefined;
  return {
    attach: (element: HTMLElement | null) => {
      list = element;
    },
    scrollTo: (index: number) => {
      const element = list as HTMLElement;
      element.scrollLeft = index * element.clientWidth;
    },
    pressStart: () => {
      dragging = false;
    },
    dragStart: () => {
      dragging = true;
    },
    tapped: () => !dragging,
  };
}
