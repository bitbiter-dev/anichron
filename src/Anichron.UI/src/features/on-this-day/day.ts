import { queryOptions } from '@tanstack/react-query';
import type { ApiClient } from '../../api/client';
import type { YearGroup } from '../../api/flashbacks';

const twoDigits = (value: number) => String(value).padStart(2, '0');

export const localDate = (now = new Date()) =>
  `${String(now.getFullYear())}-${twoDigits(now.getMonth() + 1)}-${twoDigits(now.getDate())}`;

export const utcDay = (date: string) => new Date(`${date}T00:00:00Z`);

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

export const startGroup = (years: readonly YearGroup[], year: number | undefined) =>
  Math.max(
    0,
    years.findIndex((group) => group.year === year),
  );

// Stryker disable next-line ArrowFunction,ObjectLiteral
export const storySearch = (search: Record<string, unknown>) => ({ year: Number(search.year) });
