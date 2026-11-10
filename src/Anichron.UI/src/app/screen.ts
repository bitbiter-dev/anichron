import { useMatches } from '@tanstack/react-router';

export function useScreen() {
  return useMatches({
    select: (matches) => matches.map((match) => match.staticData.screen).at(-1),
  });
}
