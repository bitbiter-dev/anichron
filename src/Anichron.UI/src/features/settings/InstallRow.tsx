import { SquarePlus } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { Modal } from '../../shared/ui/Modal';
import { RowAction } from '../../shared/ui/Row';
import { type ShownStep, useInstallRow } from './install-row';

export function InstallRow({ className }: { className: string }) {
  const { t } = useTranslation();
  const row = useInstallRow();
  if (!row) return null;
  return (
    <div className={className}>
      <RowAction icon={SquarePlus} onPress={row.press}>
        {t('settings.install.title')}
      </RowAction>
      {row.open && <HowToInstall steps={row.steps} close={row.close} />}
    </div>
  );
}

interface HowToInstallProps {
  steps: readonly ShownStep[] | undefined;
  close: () => void;
}

function HowToInstall({ steps, close }: HowToInstallProps) {
  const { t } = useTranslation();
  return (
    <Modal title={t('settings.install.title')} onClose={close}>
      {steps ? <Steps steps={steps} /> : <p>{t('settings.install.steps.android')}</p>}
      <Button variant="secondary" onPress={close} className="self-end">
        {t('settings.install.close')}
      </Button>
    </Modal>
  );
}

function Steps({ steps }: { steps: readonly ShownStep[] }) {
  const { t } = useTranslation();
  return (
    <ol className="flex flex-col gap-3">
      {steps.map(({ step, icon: Icon, shape }) => (
        <li key={step} className="flex items-center gap-3">
          <span
            aria-hidden
            className={`flex shrink-0 items-center bg-surface text-accent ${shapes[shape]}`}
          >
            <Icon aria-hidden className="size-5" />
          </span>
          {t(`settings.install.ios.${step}`)}
        </li>
      ))}
    </ol>
  );
}

const shapes = {
  button: 'size-11 justify-center rounded-full',
  row: 'h-11 w-24 justify-end rounded-field px-3',
} as const;
