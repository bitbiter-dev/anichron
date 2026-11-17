import { useMatch, useRouter } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';

export function useUpdateNotice() {
  const { updates } = useRouter().options.context;
  const waiting = useSyncExternalStore(updates.subscribe, updates.waiting);
  const story = useMatch({ from: '/signed-in/shell/on-this-day/day/$date', shouldThrow: false });
  return waiting && !story ? updates.apply : undefined;
}
