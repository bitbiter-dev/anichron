import { useState, useSyncExternalStore } from 'react';
import { listen } from '../shared/listen';

const threshold = 8;

function position() {
  const bottom = document.documentElement.scrollHeight - window.innerHeight;
  return Math.max(0, Math.min(window.scrollY, bottom));
}

function createBarVisibility() {
  let start = position();
  let last = start;
  let hidden = false;
  let notify: () => void = () => undefined;
  return {
    hidden: () => hidden,
    subscribe: (onChange: () => void) => {
      notify = onChange;
      return listen('scroll', () => {
        const y = position();
        if (y < last) start = y;
        last = y;
        hidden = y - start > threshold;
        notify();
      });
    },
    show: () => {
      start = last;
      hidden = false;
      notify();
    },
  };
}

export function useHidesOnScroll() {
  const [bar] = useState(createBarVisibility);
  return { hidden: useSyncExternalStore(bar.subscribe, bar.hidden), show: bar.show };
}
