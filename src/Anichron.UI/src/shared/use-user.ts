import { useRouter } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';

export function useUser() {
  const { session } = useRouter().options.context.api;
  return useSyncExternalStore(session.onChange, session.user);
}
