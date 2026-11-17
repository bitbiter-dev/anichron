import { useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Button } from './Button';

export function SignOutButton() {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  return (
    <Button variant="danger" onPress={() => void api.session.logout()} className="self-start px-0">
      {t('signOut')}
    </Button>
  );
}
