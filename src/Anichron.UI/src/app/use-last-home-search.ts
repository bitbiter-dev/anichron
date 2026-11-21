import { useLocation } from '@tanstack/react-router';
import { useState } from 'react';

export function useLastHomeSearch() {
  const location = useLocation();
  const [shown, setShown] = useState({ earlier: undefined as number | undefined });
  const atHome = location.pathname === '/';
  if (atHome && location.search.earlier !== shown.earlier)
    setShown({ earlier: location.search.earlier });
  return atHome ? { earlier: location.search.earlier } : shown;
}
