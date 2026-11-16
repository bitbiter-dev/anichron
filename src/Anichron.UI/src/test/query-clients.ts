import type { QueryClient } from '@tanstack/react-query';
import { vi } from 'vitest';

const rendered = new Set<QueryClient>();

export function track(queryClient: QueryClient) {
  rendered.add(queryClient);
  return queryClient;
}

export async function settleRequests() {
  const busy = (client: QueryClient) => client.isFetching() + client.isMutating() > 0;
  for (let step = 0; [...rendered].some(busy); step++) {
    if (step === 10_000)
      throw new Error('queries and mutations did not settle within 10 s of fake time.');
    await vi.advanceTimersByTimeAsync(1);
  }
  rendered.clear();
}
