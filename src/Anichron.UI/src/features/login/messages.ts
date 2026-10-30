const keys = new Map<string | undefined, MessageKey>([
  ['required', 'login.errors.required'],
  ['invalid_credentials', 'login.errors.invalidCredentials'],
  ['account_disabled', 'login.errors.accountDisabled'],
  ['account_locked', 'login.errors.accountLocked'],
  ['rate_limited', 'login.errors.rateLimited'],
]);

export type MessageKey =
  | 'login.errors.required'
  | 'login.errors.invalidCredentials'
  | 'login.errors.accountDisabled'
  | 'login.errors.accountLocked'
  | 'login.errors.rateLimited'
  | 'login.errors.generic';

export function messageKey(code: string | undefined): MessageKey {
  return keys.get(code) ?? 'login.errors.generic';
}
