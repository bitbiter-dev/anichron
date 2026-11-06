import { useTranslation } from 'react-i18next';

export function PlaceholderPage() {
  const { t } = useTranslation();
  return (
    <main className="p-4">
      <h1 className="text-title font-semibold">{t('placeholder.title')}</h1>
      <p>{t('placeholder.body')}</p>
    </main>
  );
}
