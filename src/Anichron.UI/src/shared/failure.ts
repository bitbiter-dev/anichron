import type { ParseKeys } from 'i18next';
import { problemOf, type ErrorCode } from '../api/problem';

export function failureMessage<K extends ParseKeys>(error: unknown, failed: K) {
  return problemOf(error).code === ('service_unavailable' satisfies ErrorCode)
    ? 'failure.unreachable'
    : failed;
}
