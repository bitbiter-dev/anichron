import { useRouter } from '@tanstack/react-router';
import type { ParseKeys } from 'i18next';
import { ChevronLeft } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';

interface TopBarProps {
  title: ParseKeys;
  parent?: string;
  attachLeading: (slot: HTMLElement | null) => void;
  attachTrailing: (slot: HTMLElement | null) => void;
  backReplaced: boolean;
}

export function TopBar({
  title,
  parent,
  attachLeading,
  attachTrailing,
  backReplaced,
}: TopBarProps) {
  const { t } = useTranslation();
  const bar =
    'box-content h-11 rounded-b-card pt-[env(safe-area-inset-top)] glass-card [--glass-opacity:var(--glass-bar-opacity)]';
  return (
    <>
      <div aria-hidden className={`invisible ${bar}`} />
      <header
        className={`fixed inset-x-0 top-0 z-10 mx-auto grid max-w-[inherit] grid-cols-[1fr_auto_1fr] items-center px-2 ${bar}`}
      >
        <div ref={attachLeading} className="col-start-1 flex justify-self-start">
          {parent && !backReplaced && <BackButton parent={parent} />}
        </div>
        <h1 className="col-start-2 flex min-h-11 min-w-0 items-center font-semibold">
          <span className="truncate">{t(title)}</span>
        </h1>
        <div ref={attachTrailing} className="col-start-3 flex justify-self-end" />
      </header>
    </>
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
      className="flex min-h-11 items-center gap-0.5 rounded-full pr-3 text-accent outline-none focus-visible:focus-ring"
    >
      <ChevronLeft aria-hidden className="size-6" />
      {t('shell.back')}
    </Button>
  );
}
