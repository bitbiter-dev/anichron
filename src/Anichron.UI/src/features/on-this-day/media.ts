import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';

export function useRecovering(onFailed: () => void) {
  const { media } = useRouter().options.context.api;
  const [renewal] = useState(media.renewal);
  const [reloads, setReloads] = useState(0);
  return {
    reloads,
    onError: (source: string) => {
      void renewal(source).then((renewed) => {
        if (renewed) setReloads(1);
        else onFailed();
      });
    },
  };
}

export function decoded(image: HTMLImageElement, ready: () => void) {
  void image.decode().then(ready, ready);
}
