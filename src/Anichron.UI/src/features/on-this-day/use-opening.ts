import { useRouter, useRouterState } from '@tanstack/react-router';

export function useOpening(date: string, year: number) {
  const { href } = useRouter().buildLocation({
    to: '/day/$date',
    params: { date },
    search: { year },
  });
  return useRouterState({
    select: ({ isLoading, location }) => isLoading && location.href === href,
  });
}
