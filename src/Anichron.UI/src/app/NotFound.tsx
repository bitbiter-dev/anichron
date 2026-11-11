import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { buttonStyles } from '../shared/ui/Button';

export function NotFound() {
  const { t } = useTranslation();
  return (
    <main className="grid min-h-dvh place-items-center p-4 glow-backdrop">
      <div className="flex w-full max-w-sm flex-col gap-4 rounded-card p-6 glass-card">
        <h1 className="text-title font-semibold">{t('notFound.title')}</h1>
        <p className="text-fg-muted">{t('notFound.body')}</p>
        <Link to="/" className={buttonStyles({ class: 'mt-2' })}>
          {t('notFound.home')}
        </Link>
      </div>
    </main>
  );
}
