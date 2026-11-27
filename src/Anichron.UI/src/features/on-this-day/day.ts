import { mutationOptions, queryOptions, type QueryClient } from '@tanstack/react-query';
import type { Change } from '../../api/assets';
import type { ApiClient } from '../../api/client';
import type { Day, YearGroup } from '../../api/flashbacks';
import type { Memory } from '../../api/memories';
import type { User } from '../../api/session';
import { burstsKey, daysKey } from '../../shared/memory-queries';
import { burstIdOf, coverOf } from './burst';

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

export function hasPhotos(user: User) {
  return user.hasStorageConfig;
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
  return [...daysKey(), date] as const;
}

export function dayQuery(api: ApiClient, date: string) {
  return queryOptions({
    queryKey: dayKey(date),
    queryFn: ({ signal }) => api.flashbacks.onThisDay(date, signal),
  });
}

export function burstQuery(api: ApiClient, memory: Memory) {
  const id = burstIdOf(memory);
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
  if (first) await api.media.downloads.picture(first);
}

export function framesOf(api: ApiClient, queryClient: QueryClient, memory: Memory) {
  return memory.burst ? queryClient.getQueryData(burstQuery(api, memory).queryKey) : undefined;
}

export function previewOf(api: ApiClient, queryClient: QueryClient, memory: Memory) {
  return coverOf(memory, framesOf(api, queryClient, memory)).proxies.preview;
}

const mostPages = 61;

export function homeSearch(search: Record<string, unknown>): { earlier?: number } {
  const earlier = Math.min(Math.floor(Number(search.earlier)), mostPages);
  return { earlier: earlier > 0 ? earlier : undefined };
}

// Stryker disable next-line BlockStatement
export function storySearch(search: Record<string, unknown>) {
  // Stryker disable next-line ObjectLiteral
  return { year: Number(search.year) };
}
