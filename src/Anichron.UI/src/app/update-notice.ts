import { useMatch, useRouter } from '@tanstack/react-router';
import { useEffect, useState, useSyncExternalStore } from 'react';
import { listen } from '../shared/listen';

export function useUpdateNotice() {
  const { updates } = useRouter().options.context;
  const waiting = useSyncExternalStore(updates.subscribe, updates.waiting);
  const story = useMatch({ from: '/signed-in/shell/on-this-day/day/$date', shouldThrow: false });
  const [later, setLater] = useState(false);
  useEffect(
    () =>
      listen(document, 'visibilitychange', () => {
        if (document.visibilityState === 'visible') setLater(false);
      }),
    [],
  );
  if (!waiting || story || later) return undefined;
  return {
    reload: updates.apply,
    later: () => {
      setLater(true);
    },
  };
}
