import type { Messages } from '../../shared/messages';

export function loginMessages(): Messages {
  return new Map([
    ['invalid_credentials', 'login.errors.invalidCredentials'],
    ['account_disabled', 'login.errors.accountDisabled'],
    ['account_locked', 'login.errors.accountLocked'],
  ]);
}
