import { useRouter } from '@tanstack/react-router';
import { useEffect } from 'react';
import { listen } from '../shared/listen';

export function useRetry() {
  const router = useRouter();
  const retry = () => void router.invalidate();
  useEffect(() => listen(window, 'online', retry));
  return retry;
}
