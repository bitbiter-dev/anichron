import { useQueries } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { Memory } from '../../api/memories';
import { coverOf } from './burst';
import { burstQuery } from './day';

export function useCover(memory: Memory) {
  const { api } = useRouter().options.context;
  const bursts = useQueries({
    queries: (memory.burst ? [memory] : []).map((ofBurst) => ({
      ...burstQuery(api, ofBurst),
      enabled: false,
    })),
  });
  return coverOf(memory, bursts[0]?.data);
}
