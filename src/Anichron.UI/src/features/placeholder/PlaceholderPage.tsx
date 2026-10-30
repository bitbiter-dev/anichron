import { useTranslation } from 'react-i18next';

export function PlaceholderPage() {
  const { t } = useTranslation();
  return (
    <main>
      <h1>{t('placeholder.title')}</h1>
      <p>{t('placeholder.body')}</p>
    </main>
  );
}
