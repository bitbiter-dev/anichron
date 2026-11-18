import { useRouter } from '@tanstack/react-router';
import { Ellipsis, type LucideIcon, Share, SquarePlus } from 'lucide-react';
import { useState, useSyncExternalStore } from 'react';
import type { IosOffer } from '../../app/install';

export type InstallStep = 'share' | 'viewMore' | 'more' | 'add';

function iosSteps(offer: IosOffer) {
  const steps: Record<IosOffer, readonly InstallStep[]> = {
    'ios-safari': ['share', 'viewMore', 'add'],
    'ios-chrome': ['more', 'share', 'add'],
  };
  return steps[offer];
}

function iconOf(step: InstallStep): LucideIcon {
  if (step === 'share') return Share;
  return step === 'add' ? SquarePlus : Ellipsis;
}

function shownStep(step: InstallStep) {
  return { step, icon: iconOf(step), shape: step === 'add' ? 'row' : 'button' } as const;
}

export type ShownStep = ReturnType<typeof shownStep>;

export function useInstallRow() {
  const { install } = useRouter().options.context;
  const offer = useSyncExternalStore(install.subscribe, install.offer);
  const [open, setOpen] = useState(false);
  if (offer === 'none') return undefined;
  const dialog = {
    open,
    close: () => {
      setOpen(false);
    },
  };
  if (typeof offer !== 'string') return { ...dialog, press: offer.install, steps: undefined };
  return {
    ...dialog,
    press: () => {
      setOpen(true);
    },
    steps: offer === 'android' ? undefined : iosSteps(offer).map(shownStep),
  };
}
