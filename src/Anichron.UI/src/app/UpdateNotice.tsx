import { useTranslation } from 'react-i18next';
import { Button } from '../shared/ui/Button';
import { Modal } from '../shared/ui/Modal';
import { useUpdateNotice } from './update-notice';

export function UpdateNotice() {
  const { t } = useTranslation();
  const notice = useUpdateNotice();
  if (!notice) return null;
  return (
    <Modal title={t('shell.newVersion')} onClose={notice.later}>
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onPress={notice.later}>
          {t('shell.later')}
        </Button>
        <Button onPress={notice.reload}>{t('shell.reload')}</Button>
      </div>
    </Modal>
  );
}
