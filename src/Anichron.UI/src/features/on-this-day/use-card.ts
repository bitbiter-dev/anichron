import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { Memory } from '../../api/flashbacks';
import { cardOf } from './burst';
import { burstQuery } from './day';

export function useCard(memory: Memory) {
  const { api } = useRouter().options.context;
  const { data } = useQuery({ ...burstQuery(api, memory.burst?.burstId ?? ''), enabled: false });
  return cardOf(memory, data);
}
