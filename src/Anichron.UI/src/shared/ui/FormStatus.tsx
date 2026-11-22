import type { ParseKeys } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Alert } from './Alert';

interface FormStatusProps {
  error?: ParseKeys | undefined;
  retryIn: number;
}

export function FormStatus({ error, retryIn }: FormStatusProps) {
  const { t } = useTranslation();
  return (
    <>
      <Alert message={error} />
      {retryIn > 0 && (
        <p className="text-small text-fg-muted">{t('retryIn', { count: retryIn })}</p>
      )}
    </>
  );
}
