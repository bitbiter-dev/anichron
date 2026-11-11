import { ErrorComponent, type ErrorComponentProps } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ProblemError, type Problem } from '../api/problem';
import { Button } from '../shared/ui/Button';
import { startFailureCopy } from './start-failure';
import { useRetry } from './use-retry';

export function StartFailed({ error }: ErrorComponentProps) {
  return error instanceof ProblemError ? (
    <StartFailure problem={error.problem} />
  ) : (
    <ErrorComponent error={error} />
  );
}

function StartFailure({ problem }: { problem: Problem }) {
  const { t } = useTranslation();
  const { retry, retryIn, blocked } = useRetry(problem.retryAfterSeconds);
  const copy = startFailureCopy(problem);
  return (
    <main className="grid min-h-dvh place-items-center p-4 glow-backdrop">
      <div className="flex w-full max-w-sm flex-col gap-4 rounded-card p-6 glass-card">
        <h1 className="text-title font-semibold">{t(copy.title)}</h1>
        <p className="text-fg-muted">{t(copy.body)}</p>
        {retryIn > 0 && (
          <p className="text-small text-fg-muted">{t('retryIn', { count: retryIn })}</p>
        )}
        <Button onPress={retry} isDisabled={blocked} className="mt-2">
          {t('startFailed.retry')}
        </Button>
      </div>
    </main>
  );
}
