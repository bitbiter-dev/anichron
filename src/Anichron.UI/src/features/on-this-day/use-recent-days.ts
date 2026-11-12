import { useQueries, useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { TFunction } from 'i18next';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { dayQuery, daysBefore, utcDay } from './day';

const daysPerPage = 6;
const nothingMilliseconds = 2000;

export interface RecentDay {
  date: string;
  label: string;
  years?: YearGroup[];
  retry: () => void;
}

const inUtc = (options: Intl.DateTimeFormatOptions) => ({ ...options, timeZone: 'UTC' });

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

export function useRecentDays(today: string) {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const [pages, setPages] = useState(1);
  const [quietPage, setQuietPage] = useState(1);
  const dates = Array.from({ length: pages * daysPerPage }, (_, index) =>
    daysBefore(today, index + 1),
  );
  const todays = useQuery(dayQuery(api, today));
  const days = useQueries({ queries: dates.map((date) => dayQuery(api, date)) });
  const addedNone = days.slice(-daysPerPage).every(({ data }) => data?.years.length === 0);
  useEffect(() => {
    if (!addedNone) return undefined;
    const timer = setTimeout(() => {
      setQuietPage(pages);
    }, nothingMilliseconds);
    return () => {
      clearTimeout(timer);
    };
  }, [addedNone, pages]);
  const found = dates.flatMap((date, index): RecentDay[] => {
    const { data, error, refetch } = days[index] as (typeof days)[number];
    const day = { date, label: label(t, date, index + 1), retry: () => void refetch() };
    if (error) return [day];
    return data?.years.length ? [{ ...day, years: data.years }] : [];
  });
  return {
    days: found,
    shown:
      pages > 1 || found.length > 0 || Boolean(todays.error) || Boolean(todays.data?.years.length),
    nothing: addedNone && quietPage < pages,
    earlier: () => {
      setPages(pages + 1);
    },
  };
}
