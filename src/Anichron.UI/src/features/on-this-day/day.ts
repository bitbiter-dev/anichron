import { queryOptions } from '@tanstack/react-query';
import type { ApiClient } from '../../api/client';
import type { YearGroup } from '../../api/flashbacks';

const minuteMilliseconds = 60_000;

export function localDate(now = new Date()) {
  return new Date(now.getTime() - now.getTimezoneOffset() * minuteMilliseconds)
    .toISOString()
    .slice(0, 10);
}

export function isCalendarDate(text: string) {
  const date = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === text;
}

export const yearsAgo = (date: string, year: number) => ({
  count: Number(date.slice(0, 4)) - year,
  year,
});

export const dayKey = (date: string) => ['on-this-day', date] as const;

export const dayQuery = (api: ApiClient, date: string) =>
  queryOptions({ queryKey: dayKey(date), queryFn: () => api.flashbacks.onThisDay(date) });

export const startGroup = (years: readonly YearGroup[], year: number | undefined) =>
  Math.max(
    0,
    years.findIndex((group) => group.year === year),
  );

// Stryker disable once ArrowFunction,ObjectLiteral
export const storySearch = (search: Record<string, unknown>) => ({ year: Number(search.year) });
