import type { HistoryLocation, RouterHistory } from '@tanstack/react-router';

export type Back = ReturnType<typeof createBack>;

export function createBack(history: RouterHistory) {
  const paths = new Map<number, string>();
  const record = ({ pathname, state }: HistoryLocation) => paths.set(state.__TSR_index, pathname);
  record(history.location);
  history.subscribe(({ location }) => record(location));
  return {
    to(parent: string) {
      if (paths.get(history.location.state.__TSR_index - 1) === parent) history.back();
      else history.replace(parent);
    },
  };
}
