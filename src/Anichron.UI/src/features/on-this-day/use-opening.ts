import { useRouterState } from '@tanstack/react-router';

export function storyHref(date: string, year: number) {
  return `/day/${date}?year=${String(year)}`;
}

export function useOpening() {
  return useRouterState({
    select: ({ isLoading, location }) => (isLoading ? location.href : undefined),
  });
}
