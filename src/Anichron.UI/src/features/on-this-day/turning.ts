import type { Downloads } from '../../api/media';
import { createChanges } from '../../shared/listen';

const turnMilliseconds = 4000;

type Arrivals = Pick<Downloads, 'sourceOf' | 'failed' | 'subscribe'>;

export function createTurning(others: readonly string[], downloads: Arrivals) {
  const changes = createChanges();
  const left = new Set<number>();
  let shown = 0;
  let due = false;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const after = (place: number) => (place + 1) % (others.length + 1);
  const urlAt = (place: number) => others[place - 1] as string;

  function next() {
    let place = after(shown);
    while (left.has(place)) place = after(place);
    return place;
  }

  function wait() {
    timer = setTimeout(() => {
      due = true;
      turn();
    }, turnMilliseconds);
  }

  function turn() {
    if (!due) return;
    for (let place = next(); place !== shown; place = next()) {
      if (place === 0 || downloads.sourceOf(urlAt(place)) !== null) {
        shown = place;
        due = false;
        changes.notify();
        wait();
        return;
      }
      if (!downloads.failed(urlAt(place))) return;
      left.add(place);
    }
  }

  return {
    shown: () => shown,
    standStill: (still: boolean) => {
      clearTimeout(timer);
      due = false;
      if (!still) wait();
    },
    subscribe: (listener: () => void) => {
      const stops = [changes.subscribe(listener), downloads.subscribe(turn)];
      // Stryker disable next-line BlockStatement
      return () => {
        for (const stop of stops) stop();
      };
    },
  };
}
