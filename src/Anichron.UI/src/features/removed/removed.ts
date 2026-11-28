import { infiniteQueryOptions } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import type { ApiClient } from '../../api/client';
import type { RemovedMemory, RemovedPage } from '../../api/soft-deleted';
import { failureMessage } from '../../shared/failure';
import { kindOf, memoryKind } from '../../shared/memory-kinds';

export function removedKey() {
  return ['removed'] as const;
}

export function removedQuery(api: ApiClient) {
  return infiniteQueryOptions({
    queryKey: removedKey(),
    queryFn: ({ pageParam, signal }) => api.softDeleted.page(pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last: RemovedPage) => last.next,
    staleTime: Infinity,
  });
}

export function reasonOf({ reason, lastSeenOnNas }: RemovedMemory) {
  return {
    key: `removed.reasons.${reason}`,
    when: {
      key: reason === 'Superseded' ? 'removed.removedAround' : 'removed.lastSeen',
      date: new Date(lastSeenOnNas),
      formatParams: { date: { dateStyle: 'medium' } },
    },
  } as const;
}

export function rowOf(memory: RemovedMemory) {
  return {
    memory,
    name: memory.path.slice(memory.path.lastIndexOf('/') + 1),
    kind: memoryKind(kindOf(memory)).name(memory),
    glyph: !memory.proxies.thumbnail && memory.mediaType === 'Video',
    live: memory.mediaType === 'LivePhoto',
    reason: reasonOf(memory),
  };
}

export type RemovedRow = ReturnType<typeof rowOf>;

export function failureOf(error: Error | null, loaded: boolean): ParseKeys | undefined {
  return error && !loaded ? failureMessage(error, 'removed.failed') : undefined;
}
