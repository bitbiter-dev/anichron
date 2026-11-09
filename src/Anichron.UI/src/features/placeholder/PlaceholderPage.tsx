import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Button } from '../../shared/ui/Button';

export function PlaceholderPage() {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const { data: me } = useQuery({ queryKey: ['users', 'me'], queryFn: () => api.me() });
  return (
    <main className="p-4">
      <h1 className="text-title font-semibold">{t('placeholder.title')}</h1>
      <p>{t('placeholder.body')}</p>
      {me && <p>{t('placeholder.greeting', { username: me.username })}</p>}
      <Button onPress={() => void api.logout()}>{t('placeholder.signOut')}</Button>
    </main>
  );
}
