import type { ParseKeys } from 'i18next';
import { useTranslation } from 'react-i18next';
import { tv } from './tv';

const alertStyles = tv({ base: 'text-danger' });

interface AlertProps {
  message: ParseKeys | undefined;
  className?: string;
}

export function Alert({ message, className }: AlertProps) {
  const { t } = useTranslation();
  return (
    <div role="alert" className={alertStyles({ class: className })}>
      {message && <p>{t(message)}</p>}
    </div>
  );
}
