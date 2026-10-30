import type { UseFormRegisterReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import type { MessageKey } from './messages';
import { useLoginForm } from './use-login-form';

export function LoginPage() {
  const { t } = useTranslation();
  const { register, onSubmit, formError, fieldError, retryIn, blocked } = useLoginForm();
  return (
    <main>
      <h1>{t('login.title')}</h1>
      <form onSubmit={(event) => void onSubmit(event)} noValidate>
        <FormError error={formError} retryIn={retryIn} />
        <Field
          label={t('login.usernameOrEmail')}
          autoComplete="username"
          error={fieldError('usernameOrEmail')}
          registration={register('usernameOrEmail')}
        />
        <Field
          label={t('login.password')}
          type="password"
          autoComplete="current-password"
          error={fieldError('password')}
          registration={register('password')}
        />
        <button type="submit" disabled={blocked}>
          {t('login.submit')}
        </button>
      </form>
    </main>
  );
}

function FormError({ error, retryIn }: { error: MessageKey | undefined; retryIn: number }) {
  const { t } = useTranslation();
  return (
    <div role="alert">
      {error && <p>{t(error)}</p>}
      {retryIn > 0 && <p>{t('login.retryIn', { count: retryIn })}</p>}
    </div>
  );
}

interface FieldProps {
  label: string;
  type?: string;
  autoComplete: string;
  error: MessageKey | undefined;
  registration: UseFormRegisterReturn;
}

function Field({ label, type = 'text', autoComplete, error, registration }: FieldProps) {
  const { t } = useTranslation();
  const errorId = `${registration.name}-error`;
  return (
    <div className="field">
      <label htmlFor={registration.name}>{label}</label>
      <input
        id={registration.name}
        type={type}
        autoComplete={autoComplete}
        aria-invalid={error !== undefined}
        aria-describedby={error && errorId}
        {...registration}
      />
      {error && <p id={errorId}>{t(error)}</p>}
    </div>
  );
}
