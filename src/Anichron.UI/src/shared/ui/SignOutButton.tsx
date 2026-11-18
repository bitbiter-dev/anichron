import { useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Button } from './Button';
import { RowButton } from './Row';

export function SignOutButton({ row = false }: { row?: boolean }) {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const signOut = () => void api.session.logout();
  if (row) return <RowButton onPress={signOut}>{t('signOut')}</RowButton>;
  return (
    <Button variant="danger" onPress={signOut} className="self-start px-0">
      {t('signOut')}
    </Button>
  );
}
