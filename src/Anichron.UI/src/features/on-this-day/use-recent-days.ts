import { useQuery } from '@tanstack/react-query';
import { getRouteApi, useRouter } from '@tanstack/react-router';
import type { ParseKeys, TFunction } from 'i18next';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { RecentDay as Summary } from '../../api/flashbacks';
import { daysBefore, recentDaysQuery, utcDay } from './day';
import { useGroupsShown } from './use-groups-shown';

const daysPerPage = 6;
const dayMilliseconds = 86_400_000;

export interface DayWithMemories {
  date: string;
  label: string;
  newestYear: number;
  thumbnail: string | null;
}

export interface FailedPage {
  page: number;
  retry: () => void;
}

export type RecentDay = DayWithMemories | FailedPage;

export function hasMemories(day: RecentDay): day is DayWithMemories {
  return 'newestYear' in day;
}

function inUtc(options: Intl.DateTimeFormatOptions) {
  return { ...options, timeZone: 'UTC' };
}

function label(t: TFunction, today: string, date: string) {
  const day = utcDay(date);
  if (utcDay(today).getTime() - day.getTime() === dayMilliseconds) return t('onThisDay.yesterday');
  return t('onThisDay.recentDay', {
    weekdayOf: day,
    dayOf: day,
    monthOf: day,
    formatParams: {
      weekdayOf: inUtc({ weekday: 'short' }),
      dayOf: inUtc({ day: 'numeric' }),
      monthOf: inUtc({ month: 'short' }),
    },
  });
}

function thumbnails(days: readonly Summary[]) {
  return days.map(({ memory }) => memory.proxies.thumbnail);
}

function earlierLabel(exhausted: boolean): ParseKeys {
  return exhausted ? 'onThisDay.nothingEarlier' : 'onThisDay.earlier';
}

export function useRecentDays(today: string) {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const home = getRouteApi('/signed-in/shell/on-this-day');
  const { earlier: pages = 1 } = home.useSearch();
  const navigate = home.useNavigate();
  const before = daysBefore(today, daysPerPage);
  const limit = (pages - 1) * daysPerPage;
  const first = useQuery(
    recentDaysQuery(api, { before: today, since: before, limit: daysPerPage }),
  );
  const later = useQuery({ ...recentDaysQuery(api, { before, limit }), enabled: pages > 1 });
  const fewer = useQuery({
    ...recentDaysQuery(api, { before, limit: limit - daysPerPage }),
    enabled: false,
  });
  const laterDays = later.data ?? fewer.data ?? [];
  const [exhausted, setExhausted] = useState(false);
  if (!exhausted && later.data && later.data.length < limit) setExhausted(true);
  const shownPages = useGroupsShown([
    { settled: !first.isPending, pictures: thumbnails(first.data ?? []) },
    ...Array.from({ length: pages - 1 }, (_, page) => {
      const onPage = laterDays.slice(page * daysPerPage, (page + 1) * daysPerPage);
      return {
        settled: !later.isPending || onPage.length === daysPerPage,
        pictures: thumbnails(onPage),
      };
    }),
  ]);
  const toDay = ({ date, year, memory }: Summary): DayWithMemories => ({
    date,
    label: label(t, today, date),
    newestYear: year,
    thumbnail: memory.proxies.thumbnail,
  });
  const failed = (page: number, retry: () => unknown): FailedPage[] => [
    { page, retry: () => void retry() },
  ];
  const firstDays: RecentDay[] = first.isError
    ? failed(1, first.refetch)
    : (first.data ?? []).map(toDay);
  const shownLater = laterDays.slice(0, Math.max(0, shownPages - 1) * daysPerPage).map(toDay);
  const laterFailed = later.isError;
  const days = [...firstDays, ...shownLater, ...(laterFailed ? failed(2, later.refetch) : [])];
  const loading = shownPages === 0;
  return {
    days: loading ? [] : days,
    loading,
    empty: !loading && days.length === 0,
    earlierLabel: earlierLabel(exhausted),
    exhausted,
    loadingEarlier: !loading && shownPages < pages,
    showEarlier: () => {
      void navigate({
        to: '/',
        search: (current) => ({ ...current, earlier: pages + 1 }),
        replace: true,
        resetScroll: false,
      });
    },
  } as const;
}
