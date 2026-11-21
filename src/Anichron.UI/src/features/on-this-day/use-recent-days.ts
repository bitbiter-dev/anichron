import { useQueries, useQuery } from '@tanstack/react-query';
import { getRouteApi, useRouter } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { dayQuery, daysBefore, utcDay } from './day';
import { dayView, thumbnailOf } from './day-view';
import { useGroupsShown } from './use-groups-shown';

const daysPerPage = 6;
const nothingMilliseconds = 2000;

export interface DayWithMemories {
  date: string;
  label: string;
  newestYear: number;
  thumbnail: string | null;
}

export interface FailedDay {
  date: string;
  label: string;
  retry: () => void;
}

export type RecentDay = DayWithMemories | FailedDay;

export function hasMemories(day: RecentDay): day is DayWithMemories {
  return 'newestYear' in day;
}

function inUtc(options: Intl.DateTimeFormatOptions) {
  return { ...options, timeZone: 'UTC' };
}

function label(t: TFunction, date: string, ago: number) {
  if (ago === 1) return t('onThisDay.yesterday');
  const day = utcDay(date);
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

function withMemories(date: string, label: string, newest: YearGroup): DayWithMemories {
  return {
    date,
    label,
    newestYear: newest.year,
    thumbnail: thumbnailOf(newest),
  };
}

export function useRecentDays(today: string) {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const home = getRouteApi('/signed-in/shell/on-this-day');
  const { earlier = 1 } = home.useSearch();
  const navigate = home.useNavigate();
  const [{ tappedFor, wasNothing }, setTapped] = useState({ tappedFor: 0, wasNothing: false });
  const pages = Math.max(earlier, tappedFor);
  const tapped = tappedFor === pages;
  const dates = Array.from({ length: pages * daysPerPage }, (_, index) =>
    daysBefore(today, index + 1),
  );
  const todayView = dayView(useQuery(dayQuery(api, today)));
  const days = useQueries({ queries: dates.map((date) => dayQuery(api, date)) });
  const added = days.slice(-daysPerPage);
  const addedNothing = tapped && added.every(({ data }) => data?.years.length === 0);
  const nothing = addedNothing || (wasNothing && added.some(({ isPending }) => isPending));
  useEffect(() => {
    if (!addedNothing) return undefined;
    const timer = setTimeout(() => {
      setTapped((current) => ({ ...current, tappedFor: 0 }));
    }, nothingMilliseconds);
    return () => {
      clearTimeout(timer);
    };
  }, [addedNothing]);
  const shownPages = useGroupsShown(
    Array.from({ length: pages }, (_, page) => {
      const onPage = days.slice(page * daysPerPage, (page + 1) * daysPerPage);
      return {
        settled: onPage.every(({ isPending, failureCount }) => !isPending || failureCount > 0),
        pictures: onPage.map(({ data }) => (data?.years[0] ? thumbnailOf(data.years[0]) : null)),
      };
    }),
  );
  const recent = dates.slice(0, shownPages * daysPerPage).flatMap((date, index): RecentDay[] => {
    const { data, error, refetch } = days[index] as (typeof days)[number];
    const dayLabel = label(t, date, index + 1);
    if (error) return [{ date, label: dayLabel, retry: () => void refetch() }];
    const newest = data?.years[0];
    return newest ? [withMemories(date, dayLabel, newest)] : [];
  });
  const todaySettled = Boolean(todayView.years ?? todayView.empty ?? todayView.failure);
  const loading = recent.length === 0 && (shownPages === 0 || !todaySettled);
  return {
    days: recent,
    loading,
    visible: loading || recent.length > 0 || Boolean(todayView.years ?? todayView.failure),
    earlierLabel: nothing ? 'onThisDay.nothingEarlier' : 'onThisDay.earlier',
    loadingEarlier: tapped && shownPages < pages,
    showEarlier: () => {
      setTapped({ tappedFor: pages + 1, wasNothing: nothing });
      void navigate({
        to: '/',
        search: (current) => ({ ...current, earlier: pages + 1 }),
        replace: true,
        resetScroll: false,
      });
    },
  } as const;
}
