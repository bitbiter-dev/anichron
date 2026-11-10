import { useRouter } from '@tanstack/react-router';
import { useEffect } from 'react';
import { listen } from '../shared/listen';
import { useCountdown } from '../shared/use-countdown';

export function useRetry(retryAfterSeconds: number) {
  const router = useRouter();
  const { remaining } = useCountdown(retryAfterSeconds);
  const blocked = remaining > 0;
  const retry = () => {
    if (!blocked) void router.invalidate();
  };
  useEffect(() => listen(window, 'online', retry));
  return { retry, retryIn: remaining, blocked };
}
