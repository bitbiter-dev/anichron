import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';

export function useRecovering(onFailed: () => void) {
  const { media } = useRouter().options.context.api;
  const [renewal] = useState(media.renewOnce);
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
