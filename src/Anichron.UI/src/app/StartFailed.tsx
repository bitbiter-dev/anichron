import { ErrorComponent, type ErrorComponentProps } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { ProblemError, type Problem } from '../api/problem';
import { Button } from '../shared/ui/Button';
import { CentredPage } from '../shared/ui/CentredPage';
import { FormStatus } from '../shared/ui/FormStatus';
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

function StartFailure({ problem }: { problem: Problem }) {
  const { t } = useTranslation();
  const { latest, retryIn, blocked, pending, reload } = useRetry(problem);
  const copy = startFailureCopy(latest);
  return (
    <CentredPage title={t(copy.title)} body={t(copy.body)}>
      <FormStatus retryIn={retryIn} />
      <Button
        onPress={reload}
        isDisabled={blocked}
        isPending={pending}
        className="group relative mt-2"
      >
        {t('startFailed.retry')}
        <Ring className="inset-y-0 right-4 my-auto hidden size-5 group-data-pending:block" />
      </Button>
    </CentredPage>
  );
}
