import { useQueries, useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { dayQuery, daysBefore, utcDay } from './day';
import { dayView, thumbnailOf } from './day-view';

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
  const [{ pages, tapped, wasNothing }, setPaging] = useState({
    pages: 1,
    tapped: false,
    wasNothing: false,
  });
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
      setPaging((current) => ({ ...current, tapped: false, wasNothing: false }));
    }, nothingMilliseconds);
    return () => {
      clearTimeout(timer);
    };
  }, [addedNothing]);
  const recent = dates.flatMap((date, index): RecentDay[] => {
    const { data, error, refetch } = days[index] as (typeof days)[number];
    const dayLabel = label(t, date, index + 1);
    if (error) return [{ date, label: dayLabel, retry: () => void refetch() }];
    const newest = data?.years[0];
    return newest ? [withMemories(date, dayLabel, newest)] : [];
  });
  return {
    days: recent,
    visible: recent.length > 0 || Boolean(todayView.years ?? todayView.failure),
    earlierLabel: nothing ? 'onThisDay.nothingEarlier' : 'onThisDay.earlier',
    loadingEarlier: tapped && added.some(({ isPending }) => isPending),
    showEarlier: () => {
      setPaging({ pages: pages + 1, tapped: true, wasNothing: nothing });
    },
  } as const;
}
