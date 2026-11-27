import { Link } from '@tanstack/react-router';
import type { ParseKeys } from 'i18next';
import { ChevronRight } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { Skeleton } from '../../shared/ui/Skeleton';
import { ProxyImage } from '../../shared/ui/ProxyImage';
import { Ring } from '../../shared/ui/Ring';
import {
  type DayWithMemories,
  type FailedPage,
  hasMemories,
  useRecentDays,
} from './use-recent-days';
import { useOpening } from './use-opening';

const item =
  'group flex w-18 flex-col items-center gap-1 rounded-card text-small outline-none focus-visible:focus-ring';
const circle = 'relative flex size-16 items-center justify-center rounded-full';
const ring = '-inset-1 hidden group-aria-busy:block group-data-pending:block';

export function RecentDays({ today }: { today: string }) {
  const { t } = useTranslation();
  const { loading, ...tray } = useRecentDays(today);
  return (
    <ul
      aria-label={t('onThisDay.recentDays')}
      aria-busy={loading}
      className="-mx-4 flex snap-x gap-4 overflow-x-auto border-t border-border px-4 pt-4 pb-2"
    >
      {loading ? <DaysSkeleton /> : <Days {...tray} />}
    </ul>
  );
}

type DaysProps = Omit<ReturnType<typeof useRecentDays>, 'loading'>;

function Days({ days, empty, earlierLabel, exhausted, loadingEarlier, showEarlier }: DaysProps) {
  const { t } = useTranslation();
  return (
    <>
      {days.map((day) => (
        <li
          key={hasMemories(day) ? day.date : `failed-${String(day.page)}`}
          className="shrink-0 snap-start"
        >
          {hasMemories(day) ? <DayLink {...day} /> : <Retry {...day} />}
        </li>
      ))}
      {empty && (
        <li className="flex shrink-0 items-center text-small text-fg-muted">
          {t('onThisDay.nothingRecent')}
        </li>
      )}
      <li className="shrink-0 snap-start">
        <Earlier
          label={earlierLabel}
          disabled={exhausted}
          loading={loadingEarlier}
          onPress={showEarlier}
        />
      </li>
    </>
  );
}

function DaysSkeleton() {
  return [0, 1, 2, 3, 4, 5].map((day) => (
    <li key={day} aria-hidden className="flex w-18 shrink-0 flex-col items-center gap-1">
      <Skeleton className="size-16 rounded-full" />
      <div className="flex h-5 items-center">
        <Skeleton className="h-3 w-12 rounded-full" />
      </div>
    </li>
  ));
}

function DayLink({ date, label, newestYear, thumbnail }: DayWithMemories) {
  const opening = useOpening(date, newestYear);
  return (
    <Link
      to="/day/$date"
      params={{ date }}
      search={{ year: newestYear }}
      resetScroll={false}
      aria-busy={opening}
      className={item}
    >
      <span className={`${circle} bg-surface-raised`}>
        <ProxyImage src={thumbnail} className="size-full rounded-full object-cover" />
        <Ring className={ring} />
      </span>
      {label}
    </Link>
  );
}

function Retry({ retry }: FailedPage) {
  const { t } = useTranslation();
  return (
    <Button onPress={retry} aria-label={t('onThisDay.retryDays')} className={item}>
      <span className={`${circle} bg-accent px-1 font-semibold text-on-accent`}>
        {t('failure.retry')}
      </span>
      {t('onThisDay.daysFailed')}
    </Button>
  );
}

interface EarlierProps {
  label: ParseKeys;
  disabled: boolean;
  loading: boolean;
  onPress: () => void;
}

function Earlier({ label, disabled, loading, onPress }: EarlierProps) {
  const { t } = useTranslation();
  return (
    <Button
      onPress={onPress}
      isDisabled={disabled}
      isPending={loading}
      className={`${item} disabled:text-fg-muted`}
    >
      <span className={`${circle} bg-surface-raised`}>
        <ChevronRight aria-hidden className="size-6" />
        <Ring className={ring} />
      </span>
      <span aria-live="polite">{t(label)}</span>
    </Button>
  );
}
