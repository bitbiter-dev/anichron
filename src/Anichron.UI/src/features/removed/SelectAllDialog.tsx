import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { Modal } from '../../shared/ui/Modal';
import type { RemovedScreen } from './use-removed';

interface SelectAllDialogProps {
  selectAll: RemovedScreen['selectAll'];
}

export function SelectAllDialog({ selectAll }: SelectAllDialogProps) {
  const { t } = useTranslation();
  const { asking } = selectAll;
  if (!asking) return null;
  return (
    <Modal title={t('removed.loadAll.title', { total: asking.total })} onClose={selectAll.cancel}>
      <p>{t('removed.loadAll.body', { count: asking.remaining })}</p>
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onPress={selectAll.cancel}>
          {t('removed.cancel')}
        </Button>
        <Button onPress={selectAll.confirm}>{t('removed.loadAll.confirm')}</Button>
      </div>
    </Modal>
  );
}
