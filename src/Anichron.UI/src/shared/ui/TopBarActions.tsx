import type { ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { type TopBarSide, useTopBarSlot } from '../top-bar';

interface TopBarActionsProps {
  side: TopBarSide;
  children: ReactNode;
}

export function TopBarActions({ side, children }: TopBarActionsProps) {
  const slot = useTopBarSlot(side);
  return slot && createPortal(children, slot);
}
