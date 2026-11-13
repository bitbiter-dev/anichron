import type { UseQueryResult } from '@tanstack/react-query';
import type { Day, Memory, YearGroup } from '../../api/flashbacks';
import { problemOf } from '../../api/problem';

export interface DayView {
  years?: YearGroup[];
  empty?: true;
  failure?: FailureKey;
}

type FailureKey = 'onThisDay.unreachable' | 'onThisDay.failed';

function failureKey(error: unknown): FailureKey {
  return problemOf(error).code === 'service_unavailable'
    ? 'onThisDay.unreachable'
    : 'onThisDay.failed';
}

export function dayView({ data, error }: UseQueryResult<Day>): DayView {
  if (data) return data.years.length > 0 ? { years: data.years } : { empty: true };
  return error ? { failure: failureKey(error) } : {};
}

export function thumbnailOf(group: YearGroup) {
  return (group.memories[0] as Memory).proxies.thumbnail;
}
