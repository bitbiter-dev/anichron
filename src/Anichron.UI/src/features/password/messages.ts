import type { ErrorCode, ValidationCode } from '../../api/problem';
import type en from '../../i18n/locales/en.json';

function keys(): ReadonlyMap<string | undefined, MessageKey> {
  return new Map<ErrorCode | ValidationCode, MessageKey>([
    ['required', 'password.errors.required'],
    ['mismatch', 'password.errors.mismatch'],
    ['invalid_credentials', 'password.errors.currentWrong'],
    ['password_too_short', 'password.errors.tooShort'],
    ['password_too_long', 'password.errors.tooLong'],
    ['password_pwned', 'password.errors.pwned'],
    ['rate_limited', 'password.errors.rateLimited'],
    ['service_unavailable', 'password.errors.serviceUnavailable'],
  ]);
}

export type MessageKey = `password.errors.${keyof typeof en.password.errors}`;

export function messageKey(code: string | undefined): MessageKey {
  return keys().get(code) ?? 'password.errors.generic';
}
