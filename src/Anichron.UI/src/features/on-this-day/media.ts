import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';

export function useRecovering(onFailed: () => void) {
  const { session } = useRouter().options.context.api;
  const [reloads, setReloads] = useState(0);
  return {
    reloads,
    onError: () => {
      if (reloads > 0) onFailed();
      else
        void session.renewMedia().then(() => {
          setReloads(1);
        });
    },
  };
}

export function decoded(image: HTMLImageElement, ready: () => void) {
  void image.decode().then(ready, ready);
}
