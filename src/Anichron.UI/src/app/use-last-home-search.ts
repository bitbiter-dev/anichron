import { useLocation } from '@tanstack/react-router';
import { useState } from 'react';

export function useLastHomeSearch() {
  const location = useLocation();
  const [earlier, setEarlier] = useState<number>();
  if (location.pathname === '/' && location.search.earlier !== earlier)
    setEarlier(location.search.earlier);
  return { earlier };
}
