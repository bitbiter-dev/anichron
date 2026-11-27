import { createContext, useContext, useEffect, useState } from 'react';

export type TopBarSide = 'leading' | 'trailing';

interface TopBarSlots {
  leading: HTMLElement | null;
  trailing: HTMLElement | null;
  replaceBack: (replaced: boolean) => void;
}

export const TopBarSlotsContext = createContext<TopBarSlots | null>(null);

export function useTopBarSlots() {
  const [leading, attachLeading] = useState<HTMLElement | null>(null);
  const [trailing, attachTrailing] = useState<HTMLElement | null>(null);
  const [backReplaced, replaceBack] = useState(false);
  return {
    slots: { leading, trailing, replaceBack },
    attachLeading,
    attachTrailing,
    backReplaced,
  };
}

export function useTopBarSlot(side: TopBarSide) {
  const slots = useContext(TopBarSlotsContext) as TopBarSlots;
  const { replaceBack } = slots;
  useEffect(() => {
    if (side === 'trailing') return;
    replaceBack(true);
    return () => {
      replaceBack(false);
    };
  }, [side, replaceBack]);
  return slots[side];
}
