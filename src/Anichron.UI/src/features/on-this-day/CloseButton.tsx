import { X } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';

export const roundButton =
  'flex size-11 items-center justify-center rounded-full outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';

export function CloseButton({ close }: { close: () => void }) {
  const { t } = useTranslation();
  return (
    <Button aria-label={t('story.close')} onPress={close} className={roundButton}>
      <X aria-hidden className="size-6" />
    </Button>
  );
}
