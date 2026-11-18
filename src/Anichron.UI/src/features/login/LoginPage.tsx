import { useTranslation } from 'react-i18next';
import { Alert } from '../../shared/ui/Alert';
import { Button } from '../../shared/ui/Button';
import { FormField } from '../../shared/ui/FormField';
import { useLoginForm } from './use-login-form';

export function LoginPage() {
  const { t } = useTranslation();
  const { control, onSubmit, formError, fieldError, retryIn, blocked } = useLoginForm();
  return (
    <main className="grid min-h-dvh place-items-center p-4 glow-backdrop">
      <div className="w-full max-w-sm rounded-card p-6 glass-card">
        <h1 className="mb-6 text-title font-semibold">{t('login.title')}</h1>
        <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)} noValidate>
          <Alert message={formError} />
          {retryIn > 0 && (
            <p className="text-small text-fg-muted">{t('retryIn', { count: retryIn })}</p>
          )}
          <FormField
            control={control}
            name="usernameOrEmail"
            label={t('login.usernameOrEmail')}
            autoComplete="username"
            error={fieldError('usernameOrEmail')}
          />
          <FormField
            control={control}
            name="password"
            label={t('login.password')}
            type="password"
            autoComplete="current-password"
            error={fieldError('password')}
          />
          <Button type="submit" isDisabled={blocked} className="mt-2">
            {t('login.submit')}
          </Button>
        </form>
      </div>
    </main>
  );
}
