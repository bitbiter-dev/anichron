import { useEffect } from 'react';
import { listen } from './listen';

const loadAheadPixels = 800;
const loadAheadMemories = 3;

function nearBottom() {
  const { scrollHeight } = document.documentElement;
  return window.scrollY + window.innerHeight >= scrollHeight - loadAheadPixels;
}

export function usePaging(more: () => void, count: number) {
  useEffect(() => {
    const check = () => {
      if (nearBottom()) more();
    };
    check();
    return listen(window, 'scroll', check);
  });

  return {
    nearing: (current: number) => {
      if (current >= count - loadAheadMemories) more();
    },
  };
}
