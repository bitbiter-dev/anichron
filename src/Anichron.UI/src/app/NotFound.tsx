import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { buttonStyles } from '../shared/ui/Button';
import { CentredPage } from '../shared/ui/CentredPage';

export function NotFound() {
  const { t } = useTranslation();
  return (
    <CentredPage title={t('notFound.title')} body={t('notFound.body')}>
      <Link to="/" className={buttonStyles({ class: 'mt-2' })}>
        {t('notFound.home')}
      </Link>
    </CentredPage>
  );
}
