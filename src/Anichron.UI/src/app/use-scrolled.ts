import { useSyncExternalStore } from 'react';
import { listen } from '../shared/listen';

function subscribe(onChange: () => void) {
  return listen(window, 'scroll', onChange);
}

function scrolled() {
  return window.scrollY > 0;
}

export function useScrolled() {
  return useSyncExternalStore(subscribe, scrolled);
}
