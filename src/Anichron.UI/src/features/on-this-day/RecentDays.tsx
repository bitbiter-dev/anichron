import { Link } from '@tanstack/react-router';
import type { ParseKeys } from 'i18next';
import { ChevronRight } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { ProxyImage } from './ProxyImage';
import { Ring } from './Ring';
import {
  type DayWithMemories,
  type FailedDay,
  hasMemories,
  useRecentDays,
} from './use-recent-days';
import { storyHref, useOpening } from './use-opening';

const item =
  'group flex w-18 flex-col items-center gap-1 rounded-card text-small outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';
const circle = 'relative flex size-16 items-center justify-center rounded-full';
const ring = '-inset-1 hidden group-aria-busy:block group-data-pending:block';

export function RecentDays({ today }: { today: string }) {
  const { t } = useTranslation();
  const { days, visible, earlierLabel, loadingEarlier, showEarlier } = useRecentDays(today);
  if (!visible) return null;
  return (
    <ul
      aria-label={t('onThisDay.recentDays')}
      className="-mx-4 flex snap-x gap-4 overflow-x-auto border-t border-border px-4 pt-4 pb-2"
    >
      {days.map((day) => (
        <li key={day.date} className="shrink-0 snap-start">
          {hasMemories(day) ? <DayLink {...day} /> : <Retry {...day} />}
        </li>
      ))}
      <li className="shrink-0 snap-start">
        <Earlier label={earlierLabel} loading={loadingEarlier} onPress={showEarlier} />
      </li>
    </ul>
  );
}

function DayLink({ date, label, newestYear, thumbnail }: DayWithMemories) {
  const opening = useOpening();
  return (
    <Link
      to="/day/$date"
      params={{ date }}
      search={{ year: newestYear }}
      resetScroll={false}
      aria-busy={opening === storyHref(date, newestYear)}
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

function Retry({ label, retry }: FailedDay) {
  const { t } = useTranslation();
  return (
    <Button onPress={retry} aria-label={t('onThisDay.retryDay', { day: label })} className={item}>
      <span className={`${circle} bg-accent px-1 font-semibold text-on-accent`}>
        {t('onThisDay.retry')}
      </span>
      {label}
    </Button>
  );
}

interface EarlierProps {
  label: ParseKeys;
  loading: boolean;
  onPress: () => void;
}

function Earlier({ label, loading, onPress }: EarlierProps) {
  const { t } = useTranslation();
  return (
    <Button onPress={onPress} isPending={loading} className={item}>
      <span className={`${circle} bg-surface-raised`}>
        <ChevronRight aria-hidden className="size-6" />
        <Ring className={ring} />
      </span>
      <span aria-live="polite">{t(label)}</span>
    </Button>
  );
}
