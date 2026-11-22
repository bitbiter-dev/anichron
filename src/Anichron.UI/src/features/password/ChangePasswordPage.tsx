import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { Card } from '../../shared/ui/Card';
import { CentredPage } from '../../shared/ui/CentredPage';
import { FormField } from '../../shared/ui/FormField';
import { FormStatus } from '../../shared/ui/FormStatus';
import { SignOutButton } from '../../shared/ui/SignOut';
import { usePasswordForm, type PasswordForm } from './use-password-form';

export function ChangePasswordPage() {
  const { t } = useTranslation();
  const form = usePasswordForm();
  return (
    <main className="p-4">
      <Card as="section" className="max-w-sm gap-4">
        <p role="status">{form.changed && t('password.changed')}</p>
        <PasswordFormFields form={form} />
      </Card>
    </main>
  );
}

export function ForcedPasswordPage() {
  const { t } = useTranslation();
  return (
    <CentredPage title={t('password.forcedTitle')} body={t('password.forcedBody')}>
      <PasswordFormFields form={usePasswordForm()} />
      <SignOutButton />
    </CentredPage>
  );
}

function PasswordFormFields({ form }: { form: PasswordForm }) {
  const { t } = useTranslation();
  const { control, onSubmit, formError, fieldError, retryIn, blocked } = form;
  return (
    <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)} noValidate>
      <FormStatus error={formError} retryIn={retryIn} />
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
