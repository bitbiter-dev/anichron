import { useRouter } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';

export function useInstallSection() {
  const { install } = useRouter().options.context;
  const offer = useSyncExternalStore(install.subscribe, install.offer);
  if (offer === 'none') return undefined;
  return typeof offer === 'string' ? { steps: offer } : offer;
}
