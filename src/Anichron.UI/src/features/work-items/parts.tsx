import { useTranslation } from 'react-i18next';
import type { WorkItem } from '../../api/work-items';
import { Button } from '../../shared/ui/Button';
import { useCancel } from './use-cancel';
import { at, meter } from './view';

export function Progress({ item }: { item: WorkItem }) {
  const { t } = useTranslation();
  const { value, max, text } = meter(item.progress);
  return (
    <div className="flex flex-col gap-1 text-small">
      <progress
        aria-label={t('workItems.progress')}
        value={value}
        max={max}
        className="h-1.5 w-full accent-accent"
      />
      <p className="text-fg-muted">
        {text && t('workItems.progressOf', text)} {item.progress.note}
      </p>
    </div>
  );
}

export function Cancel({ item }: { item: WorkItem }) {
  const { t } = useTranslation();
  const cancel = useCancel(item);
  return (
    <div className="flex flex-col items-start gap-1">
      <Button
        variant="danger"
        isDisabled={cancel.isPending}
        onPress={() => {
          cancel.mutate();
        }}
        aria-label={t('workItems.cancelItem', {
          kind: t(`workItems.kinds.${item.kind}`),
          ...at(item.requestedAt),
        })}
        className="px-0"
      >
        {t('workItems.cancel')}
      </Button>
      {cancel.isError && <p role="alert">{t('workItems.cancelFailed')}</p>}
    </div>
  );
}
