import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { toastActionStyles, toastStyles } from '../shared/ui/Toast';
import { useUpdateNotice } from './update-notice';

export function UpdateNotice() {
  const { t } = useTranslation();
  const apply = useUpdateNotice();
  if (!apply) return null;
  return (
    <div className="fixed inset-x-4 bottom-[calc(max(env(safe-area-inset-bottom),0.75rem)+4rem)] mx-auto flex max-w-xs justify-center">
      <div role="status" className={toastStyles()}>
        <span className="py-2">{t('shell.newVersion')}</span>
        <Button onPress={apply} className={toastActionStyles}>
          {t('shell.reload')}
        </Button>
      </div>
    </div>
  );
}
