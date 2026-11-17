import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';
import { useInstallSection } from './install-section';

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
      {'steps' in section ? (
        <p>{t(`settings.install.steps.${section.steps}`)}</p>
      ) : (
        <Button onPress={section.install} className="self-start">
          {t('settings.install.button')}
        </Button>
      )}
    </section>
  );
}
