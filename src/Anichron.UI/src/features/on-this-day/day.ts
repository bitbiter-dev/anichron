import { mutationOptions, queryOptions, type QueryClient } from '@tanstack/react-query';
import type { Change } from '../../api/assets';
import type { ApiClient } from '../../api/client';
import type { Day, Memory, YearGroup } from '../../api/flashbacks';

const twoDigits = (value: number) => String(value).padStart(2, '0');

export const localDate = (now = new Date()) =>
  `${String(now.getFullYear())}-${twoDigits(now.getMonth() + 1)}-${twoDigits(now.getDate())}`;

export const utcDay = (date: string) => new Date(`${date}T00:00:00Z`);

export function daysBefore(date: string, count: number) {
  const day = utcDay(date);
  day.setUTCDate(day.getUTCDate() - count);
  return day.toISOString().slice(0, 10);
}

export function isCalendarDate(text: string) {
  const date = utcDay(text);
  return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === text;
}

export const yearsAgo = (date: string, year: number) => ({
  count: Number(date.slice(0, 4)) - year,
  year,
});

export const dayKey = (date: string) => ['on-this-day', date] as const;

export const dayQuery = (api: ApiClient, date: string) =>
  queryOptions({ queryKey: dayKey(date), queryFn: () => api.flashbacks.onThisDay(date) });

export interface Interaction {
  id: string;
  change: Change;
}

const reverse = (change: Change): Change =>
  Object.fromEntries(Object.entries(change).map(([flag, on]) => [flag, !on]));

function apply(queryClient: QueryClient, date: string, { id, change }: Interaction) {
  const day = queryClient.getQueryData<Day>(dayKey(date)) as Day;
  queryClient.setQueryData<Day>(dayKey(date), {
    years: day.years.map((group) => ({
      ...group,
      memories: group.memories.map((memory) =>
        memory.id === id
          ? { ...memory, interaction: { ...memory.interaction, ...change } }
          : memory,
      ),
    })),
  });
}

export const interactionMutation = (api: ApiClient, queryClient: QueryClient, date: string) =>
  mutationOptions({
    mutationFn: ({ id, change }: Interaction) => api.assets.interact(id, change),
    onMutate: async (interaction) => {
      await queryClient.cancelQueries({ queryKey: dayKey(date) });
      apply(queryClient, date, interaction);
    },
    onError: (_, { id, change }) => {
      apply(queryClient, date, { id, change: reverse(change) });
    },
    onSettled: () => {
      if (queryClient.isMutating() === 1)
        void queryClient.invalidateQueries({ queryKey: dayKey(date) });
    },
  });

export const interactionOf = (day: Day, memory: Memory) =>
  day.years.flatMap((group) => group.memories).find(({ id }) => id === memory.id)?.interaction ??
  memory.interaction;

export const startGroup = (years: readonly YearGroup[], year: number | undefined) =>
  Math.max(
    0,
    years.findIndex((group) => group.year === year),
  );

// Stryker disable next-line ArrowFunction,ObjectLiteral
export const storySearch = (search: Record<string, unknown>) => ({ year: Number(search.year) });
