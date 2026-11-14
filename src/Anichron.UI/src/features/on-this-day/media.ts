import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';

export function useRecovering(onFailed: () => void) {
  const { session } = useRouter().options.context.api;
  const [reloads, setReloads] = useState(0);
  return {
    reloads,
    onError: () => {
      recover(
        reloads > 0,
        () => session.renewMedia(),
        () => {
          setReloads(1);
        },
        onFailed,
      );
    },
  };
}

export function recover(
  reloaded: boolean,
  renew: () => Promise<void>,
  reload: () => void,
  onFailed: () => void,
) {
  if (reloaded) onFailed();
  else void renew().then(reload);
}

export function decoded(image: HTMLImageElement, ready: () => void) {
  void image.decode().then(ready, ready);
}
