import type { ErrorCode, Problem } from '../api/problem';

function rateLimited() {
  return {
    title: 'startFailed.rateLimited.title',
    body: 'startFailed.rateLimited.body',
  } as const;
}

function unreachable() {
  return {
    title: 'startFailed.unreachable.title',
    body: 'startFailed.unreachable.body',
  } as const;
}

export function startFailureCopy({ code }: Problem) {
  return code === ('rate_limited' satisfies ErrorCode) ? rateLimited() : unreachable();
}
