import type { Memory } from '../../api/memories';

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

export function coverOf(memory: Memory, frames: readonly Memory[] | undefined): Memory {
  const shown = framesShown(frames) ?? [];
  const cover = shown[0];
  if (!cover) return memory;
  return { ...cover, burst: { ...(cover.burst as Burst), count: shown.length } };
}
