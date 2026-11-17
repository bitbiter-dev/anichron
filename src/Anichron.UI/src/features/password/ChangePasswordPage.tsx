import { useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Alert } from '../../shared/ui/Alert';
import { Button } from '../../shared/ui/Button';
import { FormField } from '../../shared/ui/FormField';
import { usePasswordForm, type PasswordForm } from './use-password-form';

export function ChangePasswordPage() {
  const { t } = useTranslation();
  const form = usePasswordForm();
  return (
    <main className="p-4">
      <section className="flex max-w-sm flex-col gap-4 rounded-card p-4 glass-card">
        <p role="status">{form.changed && t('password.changed')}</p>
        <PasswordFields form={form} />
      </section>
    </main>
  );
}

export function ForcedPasswordPage() {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  return (
    <main className="grid min-h-dvh place-items-center p-4 glow-backdrop">
      <div className="flex w-full max-w-sm flex-col gap-4 rounded-card p-6 glass-card">
        <h1 className="text-title font-semibold">{t('password.forcedTitle')}</h1>
        <p className="text-fg-muted">{t('password.forcedBody')}</p>
        <PasswordFields form={usePasswordForm()} />
        <Button
          variant="danger"
          onPress={() => void api.session.logout()}
          className="self-start px-0"
        >
          {t('settings.signOut')}
        </Button>
      </div>
    </main>
  );
}

function PasswordFields({ form }: { form: PasswordForm }) {
  const { t } = useTranslation();
  const { control, onSubmit, formError, fieldError, retryIn, blocked } = form;
  return (
    <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)} noValidate>
      <Alert message={formError} />
      {retryIn > 0 && (
        <p className="text-small text-fg-muted">{t('retryIn', { count: retryIn })}</p>
      )}
      <FormField
        control={control}
        name="currentPassword"
        label={t('password.current')}
        type="password"
        autoComplete="current-password"
        error={fieldError('currentPassword')}
      />
      <FormField
        control={control}
        name="newPassword"
        label={t('password.new')}
        type="password"
        autoComplete="new-password"
        error={fieldError('newPassword')}
      />
      <FormField
        control={control}
        name="confirmPassword"
        label={t('password.confirm')}
        type="password"
        autoComplete="new-password"
        error={fieldError('confirmPassword')}
      />
      <Button type="submit" isDisabled={blocked} className="mt-2">
        {t('password.submit')}
      </Button>
    </form>
  );
}
