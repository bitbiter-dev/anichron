import type { ParseKeys } from 'i18next';
import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';

interface FailedProps {
  message: ParseKeys;
  onRetry: () => void;
}

export function Failed({ message, onRetry }: FailedProps) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col items-start gap-3">
      <p>{t(message)}</p>
      <Button onPress={onRetry}>{t('onThisDay.retry')}</Button>
    </div>
  );
}
