import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';

export function useDocumentLocale() {
  const { i18n } = useTranslation();
  const locale = i18n.language;
  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);
  return locale;
}
