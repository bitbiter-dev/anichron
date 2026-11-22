import { infiniteQueryOptions } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import type { ApiClient } from '../../api/client';
import type { Memory } from '../../api/flashbacks';
import type { HiddenMemory, HiddenPage } from '../../api/hidden';
import { failureMessage } from '../../shared/failure';
import { kindOf, memoryKind } from '../../shared/memory-kinds';

export function hiddenKey() {
  return ['hidden'] as const;
}

export function hiddenQuery(api: ApiClient) {
  return infiniteQueryOptions({
    queryKey: hiddenKey(),
    queryFn: ({ pageParam, signal }) => api.hidden.page(pageParam, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last: HiddenPage) => last.next,
    staleTime: Infinity,
  });
}

function startOfDay(date: Date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

function startOfWeek(date: Date) {
  const day = startOfDay(date);
  day.setDate(day.getDate() - ((day.getDay() + 6) % 7));
  return day;
}

type Recent = 'hidden.today' | 'hidden.thisWeek' | 'hidden.thisMonth';

type Heading = { key: Recent } | { key: 'hidden.month'; month: Date };

function headingOf(hidden: Date, now: Date): Heading {
  if (hidden >= startOfDay(now)) return { key: 'hidden.today' };
  if (hidden >= startOfWeek(now)) return { key: 'hidden.thisWeek' };
  const month = new Date(hidden.getFullYear(), hidden.getMonth(), 1);
  if (month.getTime() === new Date(now.getFullYear(), now.getMonth(), 1).getTime())
    return { key: 'hidden.thisMonth' };
  return { key: 'hidden.month', month };
}

function idOf(heading: Heading) {
  return 'month' in heading ? heading.month.toISOString() : heading.key;
}

export interface Section {
  id: string;
  heading: Heading;
  items: { item: HiddenMemory; index: number }[];
}

export function sectionsOf(items: readonly HiddenMemory[], now: Date) {
  const sections: Section[] = [];
  items.forEach((item, index) => {
    const heading = headingOf(new Date(item.hiddenAt), now);
    const id = idOf(heading);
    const last = sections.at(-1);
    if (last?.id === id) last.items.push({ item, index });
    else sections.push({ id, heading, items: [{ item, index }] });
  });
  return sections;
}

export function monthFormat() {
  return { month: { month: 'long', year: 'numeric' } } as const;
}

export function takenOf({ year, month, day }: Memory) {
  return new Date(Date.UTC(year, month - 1, day));
}

export function takenFormat() {
  return { taken: { dateStyle: 'medium', timeZone: 'UTC' } } as const;
}

const daysInAMonth = 30;
const monthsInAYear = 12;

function monthsBetween(earlier: Date, later: Date) {
  return (
    (later.getFullYear() - earlier.getFullYear()) * monthsInAYear +
    later.getMonth() -
    earlier.getMonth()
  );
}

export function hiddenAgo(hiddenAt: string, now: Date) {
  const hidden = new Date(hiddenAt);
  const days = Math.round((startOfDay(now).getTime() - startOfDay(hidden).getTime()) / 86_400_000);
  const months = monthsBetween(hidden, now);
  if (days < daysInAMonth) return { value: -Math.max(0, days), range: 'day' } as const;
  if (months < monthsInAYear) return { value: -Math.max(1, months), range: 'month' } as const;
  return { value: -Math.floor(months / monthsInAYear), range: 'year' } as const;
}

export function tileOf(memory: Memory) {
  return {
    name: memoryKind(kindOf(memory)).name(memory),
    taken: takenOf(memory),
    live: memory.mediaType === 'LivePhoto',
  };
}

export function failureOf(error: Error | null): ParseKeys | undefined {
  return error ? failureMessage(error, 'hidden.failed') : undefined;
}
