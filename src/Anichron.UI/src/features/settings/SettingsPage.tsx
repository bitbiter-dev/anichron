import { useRouter } from '@tanstack/react-router';
import { useSyncExternalStore } from 'react';
import { useTranslation } from 'react-i18next';
import { themes } from '../../shared/theme';
import { RowLink } from '../../shared/ui/Row';
import { SignOutRow } from '../../shared/ui/SignOutButton';
import { SegmentedControl } from '../../shared/ui/SegmentedControl';
import { useAccount } from './account';
import { InstallRow } from './InstallRow';

const card = 'flex flex-col overflow-hidden rounded-card glass-card';
const divider = 'border-t border-border';

export function SettingsPage() {
  const { t } = useTranslation();
  const { theme } = useRouter().options.context;
  const account = useAccount();
  return (
    <main className="flex flex-col gap-6 p-4">
      <section aria-labelledby="settings-account" className={card}>
        <h2 id="settings-account" className="sr-only">
          {t('settings.account')}
        </h2>
        <Account {...account} />
        <div className={divider}>
          <RowLink to="/settings/password">{t('settings.changePassword')}</RowLink>
        </div>
      </section>
      <section className="flex flex-col gap-3 rounded-card p-4 glass-card">
        <h2 className="font-semibold">{t('settings.appearance')}</h2>
        <SegmentedControl
          label={t('settings.theme')}
          options={themes().map((value) => ({ value, label: t(`settings.themes.${value}`) }))}
          value={useSyncExternalStore(theme.subscribe, theme.get)}
          onChange={theme.choose}
        />
      </section>
      <InstallRow className={card} />
      {account.isAdmin && <Administration />}
      <div className={card}>
        <SignOutRow />
      </div>
    </main>
  );
}

interface AccountProps {
  name: string;
  initial: string;
  isAdmin: boolean;
}

function Account({ name, initial, isAdmin }: AccountProps) {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-3 p-4">
      <span
        aria-hidden
        className="flex size-11 items-center justify-center rounded-full bg-accent font-semibold text-on-accent"
      >
        {initial}
      </span>
      <div className="flex flex-col">
        <span className="font-semibold">{name}</span>
        {isAdmin && <span className="text-small text-fg-muted">{t('settings.admin')}</span>}
      </div>
    </div>
  );
}

function Administration() {
  const { t } = useTranslation();
  return (
    <section aria-labelledby="settings-administration" className={card}>
      <h2 id="settings-administration" className="p-4 font-semibold">
        {t('settings.administration')}
      </h2>
      <div className={divider}>
        <RowLink to="/admin/work-items">{t('settings.workItems')}</RowLink>
      </div>
    </section>
  );
}
