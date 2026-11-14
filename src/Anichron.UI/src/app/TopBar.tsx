import { useRouter } from '@tanstack/react-router';
import type { ParseKeys } from 'i18next';
import { ChevronLeft } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';

interface TopBarProps {
  title: ParseKeys;
  parent?: string;
}

export function TopBar({ title, parent }: TopBarProps) {
  const { t } = useTranslation();
  return (
    <header className="sticky top-0 z-10 grid grid-cols-[1fr_auto_1fr] items-center rounded-b-card px-2 pt-[env(safe-area-inset-top)] glass-card">
      {parent && <BackButton parent={parent} />}
      <h1 className="col-start-2 flex min-h-11 items-center font-semibold">{t(title)}</h1>
    </header>
  );
}

function BackButton({ parent }: { parent: string }) {
  const { t } = useTranslation();
  const { back } = useRouter().options.context;
  return (
    <Button
      onPress={() => {
        back.to(parent);
      }}
      className="flex min-h-11 items-center gap-0.5 justify-self-start rounded-full pr-3 text-accent outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <ChevronLeft aria-hidden className="size-6" />
      {t('shell.back')}
    </Button>
  );
}
