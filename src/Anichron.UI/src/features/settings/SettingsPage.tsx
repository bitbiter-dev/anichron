import { useRouter } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';
import { useTranslation } from 'react-i18next';
import { themes } from '../../shared/theme';
import { Button } from '../../shared/ui/Button';
import { SegmentedControl } from '../../shared/ui/SegmentedControl';

export function SettingsPage() {
  const { t } = useTranslation();
  const { api, theme } = useRouter().options.context;
  return (
    <main className="flex flex-col gap-6 p-4">
      <section className="flex flex-col gap-3 rounded-card p-4 glass-card">
        <h2 className="font-semibold">{t('settings.appearance')}</h2>
        <SegmentedControl
          label={t('settings.theme')}
          options={themes.map((value) => ({ value, label: t(`settings.themes.${value}`) }))}
          value={useSyncExternalStore(theme.subscribe, theme.get)}
          onChange={theme.choose}
        />
      </section>
      <section className="flex flex-col gap-3 rounded-card p-4 glass-card">
        <h2 className="font-semibold">{t('settings.account')}</h2>
        <Button
          variant="danger"
          onPress={() => void api.session.logout()}
          className="self-start px-0"
        >
          {t('settings.signOut')}
        </Button>
      </section>
    </main>
  );
}
