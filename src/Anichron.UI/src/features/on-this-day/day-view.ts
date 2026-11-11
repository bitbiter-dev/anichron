import type { UseQueryResult } from '@tanstack/react-query';
import type { Day, Memory, YearGroup } from '../../api/flashbacks';
import { problemOf } from '../../api/problem';

export interface DayView {
  years?: YearGroup[];
  empty?: true;
  failure?: FailureKey;
}

type FailureKey = 'onThisDay.unreachable' | 'onThisDay.failed';

const failureKey = (error: unknown): FailureKey =>
  problemOf(error).code === 'service_unavailable' ? 'onThisDay.unreachable' : 'onThisDay.failed';

export function dayView({ data, error }: UseQueryResult<Day>): DayView {
  if (data) return data.years.length > 0 ? { years: data.years } : { empty: true };
  return error ? { failure: failureKey(error) } : {};
}

export const thumbnailOf = (group: YearGroup) => (group.memories[0] as Memory).proxies.thumbnail;
