import { useTranslation } from 'react-i18next';
import { OnThisDayPage } from '../features/on-this-day/OnThisDayPage';
import { Button } from '../shared/ui/Button';
import { Modal } from '../shared/ui/Modal';
import { useHome } from './use-home';

export function Home() {
  const home = useHome();
  if (home.onThisDay) return <OnThisDayPage />;
  return <main>{home.notice && <NoPhotosNotice onOk={home.dismiss} />}</main>;
}

function NoPhotosNotice({ onOk }: { onOk: () => void }) {
  const { t } = useTranslation();
  return (
    <Modal role="alertdialog" title={t('shell.noPhotos')} onClose={onOk}>
      <p>{t('shell.noPhotosHint')}</p>
      <div className="flex justify-end">
        <Button onPress={onOk}>{t('shell.ok')}</Button>
      </div>
    </Modal>
  );
}
