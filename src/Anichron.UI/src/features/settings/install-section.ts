import { useRouter } from '@tanstack/react-router';
import { useState, useSyncExternalStore } from 'react';
import type { IosOffer } from '../../app/install';

export type InstallStep = 'share' | 'showMore' | 'more' | 'add';

function iosSteps(offer: IosOffer): readonly InstallStep[] {
  if (offer === 'ios-chrome') return ['more', 'share', 'add'];
  return offer === 'ios-firefox' ? ['share', 'add'] : ['share', 'showMore', 'add'];
}

export function useInstallSection() {
  const { install } = useRouter().options.context;
  const offer = useSyncExternalStore(install.subscribe, install.offer);
  const [open, setOpen] = useState(false);
  if (offer === 'none') return undefined;
  if (offer === 'android') return { text: offer };
  if (typeof offer !== 'string') return offer;
  return {
    steps: iosSteps(offer),
    open,
    show: () => {
      setOpen(true);
    },
    close: () => {
      setOpen(false);
    },
  };
}
