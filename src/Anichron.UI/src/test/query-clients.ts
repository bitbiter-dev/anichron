import type { QueryClient } from '@tanstack/react-query';
import { vi } from 'vitest';

const rendered = new Set<QueryClient>();
const loading = new Set<Promise<unknown>>();

export function track(queryClient: QueryClient) {
  rendered.add(queryClient);
  return queryClient;
}

export function settled<T>(load: Promise<T>) {
  loading.add(load);
  void load.finally(() => loading.delete(load));
  return load;
}

export async function settleRequests() {
  const busy = (client: QueryClient) => client.isFetching() + client.isMutating() > 0;
  for (let step = 0; [...rendered].some(busy) || loading.size > 0; step++) {
    if (step === 10_000)
      throw new Error('queries, mutations and pictures did not settle within 10 s of fake time.');
    await vi.advanceTimersByTimeAsync(1);
  }
}

export function forgetQueryClients() {
  rendered.clear();
}
