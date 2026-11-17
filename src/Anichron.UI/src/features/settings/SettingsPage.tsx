import { Link, useRouter, type LinkProps } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';
import { useTranslation } from 'react-i18next';
import { themes } from '../../shared/theme';
import { SignOutButton } from '../../shared/ui/SignOutButton';
import { useUser } from '../../shared/use-user';
import { SegmentedControl } from '../../shared/ui/SegmentedControl';

export function SettingsPage() {
  const { t } = useTranslation();
  const { theme } = useRouter().options.context;
  return (
    <main className="flex flex-col gap-6 p-4">
      <section className="flex flex-col gap-3 rounded-card p-4 glass-card">
        <h2 className="font-semibold">{t('settings.appearance')}</h2>
        <SegmentedControl
          label={t('settings.theme')}
          options={themes().map((value) => ({ value, label: t(`settings.themes.${value}`) }))}
          value={useSyncExternalStore(theme.subscribe, theme.get)}
          onChange={theme.choose}
        />
      </section>
      {useUser()?.isAdmin && <Administration />}
      <section className="flex flex-col gap-3 rounded-card p-4 glass-card">
        <h2 className="font-semibold">{t('settings.account')}</h2>
        <SettingsLink to="/settings/password">{t('settings.changePassword')}</SettingsLink>
        <SignOutButton />
      </section>
    </main>
  );
}

function Administration() {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-3 rounded-card p-4 glass-card">
      <h2 className="font-semibold">{t('settings.administration')}</h2>
      <SettingsLink to="/admin/work-items">{t('settings.workItems')}</SettingsLink>
    </section>
  );
}

function SettingsLink({ to, children }: { to: LinkProps['to']; children: string }) {
  return (
    <Link
      to={to}
      className="self-start font-semibold text-accent outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      {children}
    </Link>
  );
}
