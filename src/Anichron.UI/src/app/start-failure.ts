import type { ErrorCode, Problem } from '../api/problem';

const rateLimited = {
  title: 'startFailed.rateLimited.title',
  body: 'startFailed.rateLimited.body',
} as const;

const unreachable = {
  title: 'startFailed.unreachable.title',
  body: 'startFailed.unreachable.body',
} as const;

export function startFailureCopy({ code }: Problem) {
  return code === ('rate_limited' satisfies ErrorCode) ? rateLimited : unreachable;
}
