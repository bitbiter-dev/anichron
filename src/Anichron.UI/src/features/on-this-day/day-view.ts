import type { UseQueryResult } from '@tanstack/react-query';
import type { ParseKeys } from 'i18next';
import type { Day, Memory, YearGroup } from '../../api/flashbacks';
import { failureMessage } from '../../shared/failure';

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
