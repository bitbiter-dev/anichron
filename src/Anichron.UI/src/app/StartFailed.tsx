import { ErrorComponent, type ErrorComponentProps } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ProblemError, type Problem } from '../api/problem';
import { Button } from '../shared/ui/Button';
import { Ring } from '../shared/ui/Ring';
import { startFailureCopy } from './start-failure';
import { useRetry } from './use-retry';

export function StartFailed({ error }: ErrorComponentProps) {
  return error instanceof ProblemError ? (
    <StartFailure problem={error.problem} />
  ) : (
    <ErrorComponent error={error} />
  );
}

function StartFailure({ problem: failure }: { problem: Problem }) {
  const { t } = useTranslation();
  const { problem, retryIn, blocked, pending, reload } = useRetry(failure);
  const copy = startFailureCopy(problem);
  return (
    <main className="grid min-h-dvh place-items-center p-4 glow-backdrop">
      <div className="flex w-full max-w-sm flex-col gap-4 rounded-card p-6 glass-card">
        <h1 className="text-title font-semibold">{t(copy.title)}</h1>
        <p className="text-fg-muted">{t(copy.body)}</p>
        {retryIn > 0 && (
          <p className="text-small text-fg-muted">{t('retryIn', { count: retryIn })}</p>
        )}
        <Button
          onPress={reload}
          isDisabled={blocked}
          isPending={pending}
          className="group relative mt-2"
        >
          {t('startFailed.retry')}
          <Ring className="inset-y-0 right-4 my-auto hidden size-5 group-data-pending:block" />
        </Button>
      </div>
    </main>
  );
}
