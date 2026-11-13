import { queryOptions } from '@tanstack/react-query';
import type { ApiClient } from '../../api/client';
import type { YearGroup } from '../../api/flashbacks';

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
  return queryOptions({ queryKey: dayKey(date), queryFn: () => api.flashbacks.onThisDay(date) });
}

export function startGroup(years: readonly YearGroup[], year: number | undefined) {
  return Math.max(
    0,
    years.findIndex((group) => group.year === year),
  );
}

// Stryker disable next-line BlockStatement
export function storySearch(search: Record<string, unknown>) {
  // Stryker disable next-line ObjectLiteral
  return { year: Number(search.year) };
}
