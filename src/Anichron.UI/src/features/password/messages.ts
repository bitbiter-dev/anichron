import type { Messages } from '../../shared/messages';

export function passwordMessages(): Messages {
  return new Map([
    ['mismatch', 'password.errors.mismatch'],
    ['invalid_credentials', 'password.errors.currentWrong'],
    ['password_too_short', 'password.errors.tooShort'],
    ['password_too_long', 'password.errors.tooLong'],
    ['password_pwned', 'password.errors.pwned'],
  ]);
}
