import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { Modal } from '../../shared/ui/Modal';
import { useFocusOnOpen } from './use-focus-on-open';
import type { RemovedScreen } from './use-removed';

interface ForgetDialogProps {
  forget: RemovedScreen['forget'];
}

export function ForgetDialog({ forget }: ForgetDialogProps) {
  return forget.asked > 0 && <Confirmation forget={forget} />;
}

function Confirmation({ forget }: ForgetDialogProps) {
  const { t } = useTranslation();
  const cancel = useFocusOnOpen<HTMLButtonElement>();
  const count = forget.asked;
  return (
    <Modal alert title={t('removed.confirm.title', { count })} onClose={forget.cancel}>
      <p>{t('removed.confirm.body', { count })}</p>
      <div className="flex justify-end gap-2">
        <Button ref={cancel} variant="secondary" onPress={forget.cancel}>
          {t('removed.cancel')}
        </Button>
        <Button variant="danger" onPress={forget.confirm}>
          {t('removed.forget')}
        </Button>
      </div>
    </Modal>
  );
}
