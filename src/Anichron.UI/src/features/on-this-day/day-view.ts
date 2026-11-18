import { type UseQueryResult, useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { ParseKeys } from 'i18next';
import type { Day, Memory, YearGroup } from '../../api/flashbacks';
import { failureMessage } from '../../shared/failure';
import { dayQuery } from './day';
import { useLoadedPictures } from './use-pictures';

export interface DayView {
  years?: YearGroup[];
  empty?: true;
  failure?: ParseKeys;
}

export function dayView({ data, error }: UseQueryResult<Day>): DayView {
  if (data) return data.years.length > 0 ? { years: data.years } : { empty: true };
  return error ? { failure: failureMessage(error, 'onThisDay.failed') } : {};
}

export function thumbnailOf(group: YearGroup) {
  return (group.memories[0] as Memory).proxies.thumbnail;
}

export function useDayView(date: string) {
  const { api } = useRouter().options.context;
  const day = useQuery(dayQuery(api, date));
  const view = dayView(day);
  const thumbnails = (view.years ?? []).map(thumbnailOf);
  const loaded = useLoadedPictures(thumbnails);
  return { view: loaded(thumbnails) ? view : {}, retry: () => void day.refetch() };
}
