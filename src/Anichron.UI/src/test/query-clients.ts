import type { QueryClient } from '@tanstack/react-query';
import { vi } from 'vitest';

const rendered = new Set<QueryClient>();

export function track(queryClient: QueryClient) {
  rendered.add(queryClient);
  return queryClient;
}

export async function settleQueries() {
  for (let step = 0; [...rendered].some((client) => client.isFetching() > 0); step++) {
    if (step === 10_000) throw new Error('queries did not settle within 10 s of fake time.');
    await vi.advanceTimersByTimeAsync(1);
  }
  rendered.clear();
}
