import { mutationOptions, queryOptions, type QueryClient } from '@tanstack/react-query';
import type { Change } from '../../api/assets';
import type { ApiClient } from '../../api/client';
import type { Day, Memory, YearGroup } from '../../api/flashbacks';
import { cardOf } from './burst';

function twoDigits(value: number) {
  return String(value).padStart(2, '0');
}

export function localDate(now = new Date()) {
  return `${String(now.getFullYear())}-${twoDigits(now.getMonth() + 1)}-${twoDigits(now.getDate())}`;
}

export function utcDay(date: string) {
  return new Date(`${date}T00:00:00Z`);
}

export function daysBefore(date: string, count: number) {
  const day = utcDay(date);
  day.setUTCDate(day.getUTCDate() - count);
  return day.toISOString().slice(0, 10);
}

export function isCalendarDate(text: string) {
  const date = utcDay(text);
  return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === text;
}

export function yearsAgo(date: string, year: number) {
  return {
    count: Number(date.slice(0, 4)) - year,
    year,
  };
}

export function dayKey(date: string) {
  return ['on-this-day', date] as const;
}

export function dayQuery(api: ApiClient, date: string) {
  return queryOptions({
    queryKey: dayKey(date),
    queryFn: ({ signal }) => api.flashbacks.onThisDay(date, signal),
  });
}

function burstsKey() {
  return ['burst'] as const;
}

export function burstQuery(api: ApiClient, id: string) {
  return queryOptions({
    queryKey: [...burstsKey(), id],
    queryFn: () => api.bursts.frames(id),
    gcTime: Infinity,
  });
}

export function pictureQuery(api: ApiClient, url: string) {
  return queryOptions({
    queryKey: [url],
    queryFn: () => api.media.loadImage(url),
    staleTime: Infinity,
    gcTime: Infinity,
  });
}

export interface Interaction {
  id: string;
  change: Change;
}

function reverse(change: Change): Change {
  return Object.fromEntries(Object.entries(change).map(([flag, on]) => [flag, !on]));
}

function apply(queryClient: QueryClient, date: string, { id, change }: Interaction) {
  const changed = (memory: Memory) =>
    memory.id === id ? { ...memory, interaction: { ...memory.interaction, ...change } } : memory;
  const day = queryClient.getQueryData<Day>(dayKey(date)) as Day;
  queryClient.setQueryData<Day>(dayKey(date), {
    years: day.years.map((group) => ({ ...group, memories: group.memories.map(changed) })),
  });
  queryClient.setQueriesData<Memory[]>({ queryKey: burstsKey() }, (frames) => frames?.map(changed));
}

export function interactionMutation(api: ApiClient, queryClient: QueryClient, date: string) {
  return mutationOptions({
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
}

export function interactionOf(day: Day, memory: Memory) {
  return (
    day.years.flatMap((group) => group.memories).find(({ id }) => id === memory.id)?.interaction ??
    memory.interaction
  );
}

export function startGroup(years: readonly YearGroup[], year: number | undefined) {
  return Math.max(
    0,
    years.findIndex((group) => group.year === year),
  );
}

export async function loadStory(
  api: ApiClient,
  queryClient: QueryClient,
  date: string,
  year: number,
) {
  const day = queryClient.getQueryData<Day>(dayKey(date));
  const group = day?.years[startGroup(day.years, year)];
  if (!group) return;
  const first = previewOf(api, queryClient, group.memories[0] as Memory);
  if (first) await api.media.loadPicture(first);
}

export function previewOf(api: ApiClient, queryClient: QueryClient, memory: Memory) {
  const frames = queryClient.getQueryData(burstQuery(api, String(memory.burst?.burstId)).queryKey);
  return cardOf(memory, frames).proxies.preview;
}

// Stryker disable next-line BlockStatement
export function storySearch(search: Record<string, unknown>) {
  // Stryker disable next-line ObjectLiteral
  return { year: Number(search.year) };
}
