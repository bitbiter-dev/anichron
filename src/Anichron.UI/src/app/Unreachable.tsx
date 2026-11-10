import { ErrorComponent, type ErrorComponentProps } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ServerUnreachable } from '../api/client';
import { Button } from '../shared/ui/Button';
import { useRetry } from './use-retry';

export function StartFailed({ error }: ErrorComponentProps) {
  return error instanceof ServerUnreachable ? <Unreachable /> : <ErrorComponent error={error} />;
}

function Unreachable() {
  const { t } = useTranslation();
  const retry = useRetry();
  return (
    <main className="grid min-h-dvh place-items-center p-4 glow-backdrop">
      <div className="flex w-full max-w-sm flex-col gap-4 rounded-card p-6 glass-card">
        <h1 className="text-title font-semibold">{t('unreachable.title')}</h1>
        <p className="text-fg-muted">{t('unreachable.body')}</p>
        <Button onPress={retry} className="mt-2">
          {t('unreachable.retry')}
        </Button>
      </div>
    </main>
  );
}
