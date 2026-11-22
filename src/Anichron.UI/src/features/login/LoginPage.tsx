import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { CentredPage } from '../../shared/ui/CentredPage';
import { FormField } from '../../shared/ui/FormField';
import { FormStatus } from '../../shared/ui/FormStatus';
import { useLoginForm } from './use-login-form';

export function LoginPage() {
  const { t } = useTranslation();
  const { control, onSubmit, formError, fieldError, retryIn, blocked } = useLoginForm();
  return (
    <CentredPage title={t('login.title')}>
      <form
        className="mt-2 flex flex-col gap-4"
        onSubmit={(event) => void onSubmit(event)}
        noValidate
      >
        <FormStatus error={formError} retryIn={retryIn} />
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
    </CentredPage>
  );
}
