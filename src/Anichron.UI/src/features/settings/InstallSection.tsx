import { Ellipsis, Share, SquarePlus } from 'lucide-react';
import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { Modal } from '../../shared/ui/Modal';
import { type InstallStep, useInstallSection } from './install-section';

type Section = NonNullable<ReturnType<typeof useInstallSection>>;

export function InstallSection() {
  const { t } = useTranslation();
  const heading = useId();
  const section = useInstallSection();
  if (!section) return null;
  return (
    <section aria-labelledby={heading} className="flex flex-col gap-3 rounded-card p-4 glass-card">
      <h2 id={heading} className="font-semibold">
        {t('settings.install.title')}
      </h2>
      <Offer section={section} />
    </section>
  );
}

function Offer({ section }: { section: Section }) {
  const { t } = useTranslation();
  if ('text' in section) return <p>{t('settings.install.steps.android')}</p>;
  if ('install' in section)
    return (
      <Button onPress={section.install} className="self-start">
        {t('settings.install.button')}
      </Button>
    );
  return (
    <>
      <Button onPress={section.show} className="self-start">
        {t('settings.install.show')}
      </Button>
      {section.open && <Steps steps={section.steps} close={section.close} />}
    </>
  );
}

function Steps({ steps, close }: { steps: readonly InstallStep[]; close: () => void }) {
  const { t } = useTranslation();
  return (
    <Modal title={t('settings.install.title')} onClose={close}>
      <ol className="flex flex-col gap-3">
        {steps.map((step) => (
          <li key={step} className="flex items-center gap-3">
            <StepIcon step={step} />
            {t(`settings.install.ios.${step}`)}
          </li>
        ))}
      </ol>
      <Button variant="secondary" onPress={close} className="self-end">
        {t('settings.install.close')}
      </Button>
    </Modal>
  );
}

const icons = { share: Share, showMore: Ellipsis, more: Ellipsis, add: SquarePlus } as const;

const shapes = {
  button: 'size-11 justify-center rounded-full',
  row: 'h-11 w-24 justify-end rounded-field px-3',
} as const;

function StepIcon({ step }: { step: InstallStep }) {
  const Icon = icons[step];
  const shape = step === 'add' ? shapes.row : shapes.button;
  return (
    <span aria-hidden className={`flex shrink-0 items-center bg-fg/10 text-accent ${shape}`}>
      <Icon aria-hidden className="size-5" />
    </span>
  );
}
