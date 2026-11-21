import { useRouter } from '@tanstack/react-router';
import { useState, useSyncExternalStore } from 'react';
import type { Problem } from '../api/problem';
import { useCountdown } from '../shared/use-countdown';
import { createStartRetry } from './start-retry';

export function useRetry(failure: Problem) {
  const { api, updates } = useRouter().options.context;
  const countdown = useCountdown(failure.retryAfterSeconds);
  const [retry] = useState(() =>
    createStartRetry(
      () => api.session.restore(),
      failure,
      (seconds) => {
        countdown.start(seconds);
      },
    ),
  );
  const { problem, pending } = useSyncExternalStore(retry.subscribe, retry.state);
  const [reloading, setReloading] = useState(false);
  return {
    latest: problem,
    retryIn: countdown.remaining,
    blocked: countdown.remaining > 0,
    pending: pending || reloading,
    reload: () => {
      setReloading(true);
      updates.reload();
    },
  };
}
