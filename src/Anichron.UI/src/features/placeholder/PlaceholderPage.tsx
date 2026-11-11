import { useTranslation } from 'react-i18next';
import { useUser } from '../../shared/use-user';

export function PlaceholderPage() {
  const { t } = useTranslation();
  const user = useUser();
  return (
    <main className="p-4">
      <h1 className="text-title font-semibold">{t('placeholder.title')}</h1>
      <p>{t('placeholder.body')}</p>
      {user && <p>{t('placeholder.greeting', { username: user.username })}</p>}
    </main>
  );
}
