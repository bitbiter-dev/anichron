import type { ParseKeys } from 'i18next';
import type { ErrorCode, ValidationCode } from '../api/problem';

export type Messages = ReadonlyMap<ErrorCode | ValidationCode, ParseKeys>;

function shared(): Messages {
  return new Map<ErrorCode | ValidationCode, ParseKeys>([
    ['required', 'errors.required'],
    ['rate_limited', 'errors.rateLimited'],
    ['service_unavailable', 'errors.serviceUnavailable'],
  ]);
}

export function messageKey(code: string | undefined, own: Messages): ParseKeys {
  const known = code as ErrorCode | ValidationCode;
  return own.get(known) ?? shared().get(known) ?? 'errors.generic';
}
