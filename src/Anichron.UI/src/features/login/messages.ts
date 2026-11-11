import type { ErrorCode, ValidationCode } from '../../api/problem';
import type en from '../../i18n/locales/en.json';

function keys(): ReadonlyMap<string | undefined, MessageKey> {
  return new Map<ErrorCode | ValidationCode, MessageKey>([
    ['required', 'login.errors.required'],
    ['invalid_credentials', 'login.errors.invalidCredentials'],
    ['account_disabled', 'login.errors.accountDisabled'],
    ['account_locked', 'login.errors.accountLocked'],
    ['rate_limited', 'login.errors.rateLimited'],
    ['service_unavailable', 'login.errors.serviceUnavailable'],
  ]);
}

export type MessageKey = `login.errors.${keyof typeof en.login.errors}`;

export function messageKey(code: string | undefined): MessageKey {
  return keys().get(code) ?? 'login.errors.generic';
}
