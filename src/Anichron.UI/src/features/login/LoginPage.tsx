import type { UseFormRegisterReturn } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import type { MessageKey } from './messages';
import { useLoginForm } from './use-login-form';

const focusRing =
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';

export function LoginPage() {
  const { t } = useTranslation();
  const { register, onSubmit, formError, fieldError, retryIn, blocked } = useLoginForm();
  return (
    <main className="grid min-h-dvh place-items-center p-4">
      <div className="w-full max-w-sm rounded-card border border-border bg-surface-raised p-6">
        <h1 className="mb-6 text-title font-semibold">{t('login.title')}</h1>
        <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)} noValidate>
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
          <button
            type="submit"
            disabled={blocked}
            className={`mt-2 min-h-11 rounded-full bg-accent px-6 font-semibold text-on-accent transition-opacity hover:opacity-90 disabled:opacity-50 ${focusRing}`}
          >
            {t('login.submit')}
          </button>
        </form>
      </div>
    </main>
  );
}

function FormError({ error, retryIn }: { error: MessageKey | undefined; retryIn: number }) {
  const { t } = useTranslation();
  return (
    <>
      <div role="alert" className="text-danger">
        {error && <p>{t(error)}</p>}
      </div>
      {retryIn > 0 && (
        <p className="text-small text-fg-muted">{t('login.retryIn', { count: retryIn })}</p>
      )}
    </>
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
    <div className="flex flex-col gap-1.5">
      <label htmlFor={registration.name} className="text-small font-medium">
        {label}
      </label>
      <input
        className={`min-h-11 rounded-field border border-border bg-surface px-3 text-fg aria-invalid:border-danger ${focusRing}`}
        id={registration.name}
        type={type}
        autoComplete={autoComplete}
        aria-invalid={error !== undefined}
        aria-describedby={error && errorId}
        {...registration}
      />
      {error && (
        <p id={errorId} className="text-small text-danger">
          {t(error)}
        </p>
      )}
    </div>
  );
}
