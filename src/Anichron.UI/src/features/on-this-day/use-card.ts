import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { Memory } from '../../api/flashbacks';
import { cardOf } from './burst';
import { burstQuery } from './day';

export function useCard(memory: Memory) {
  const { api } = useRouter().options.context;
  const burst = burstQuery(api, String(memory.burst?.burstId));
  const { data } = useQuery({ ...burst, enabled: false });
  return cardOf(memory, data);
}
