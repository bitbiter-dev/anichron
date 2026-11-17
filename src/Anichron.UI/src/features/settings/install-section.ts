import { useRouter } from '@tanstack/react-router';
import { Ellipsis, type LucideIcon, Share, SquarePlus } from 'lucide-react';
import { useState, useSyncExternalStore } from 'react';
import type { IosOffer } from '../../app/install';

export type InstallStep = 'share' | 'showMore' | 'more' | 'add';

function iosSteps(offer: IosOffer): readonly InstallStep[] {
  if (offer === 'ios-chrome') return ['more', 'share', 'add'];
  return offer === 'ios-firefox' ? ['share', 'add'] : ['share', 'showMore', 'add'];
}

function iconOf(step: InstallStep): LucideIcon {
  if (step === 'share') return Share;
  return step === 'add' ? SquarePlus : Ellipsis;
}

function shownStep(step: InstallStep) {
  return { step, icon: iconOf(step), shape: step === 'add' ? 'row' : 'button' } as const;
}

export type ShownStep = ReturnType<typeof shownStep>;

export function useInstallSection() {
  const { install } = useRouter().options.context;
  const offer = useSyncExternalStore(install.subscribe, install.offer);
  const [open, setOpen] = useState(false);
  if (offer === 'none') return undefined;
  if (offer === 'android') return { kind: 'android' } as const;
  if (typeof offer !== 'string') return { kind: 'prompt', install: offer.install } as const;
  return {
    kind: 'steps',
    steps: iosSteps(offer).map(shownStep),
    open,
    show: () => {
      setOpen(true);
    },
    close: () => {
      setOpen(false);
    },
  } as const;
}
