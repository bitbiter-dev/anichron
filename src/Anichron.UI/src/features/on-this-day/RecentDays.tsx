import { Link } from '@tanstack/react-router';
import type { ParseKeys } from 'i18next';
import { ChevronRight } from 'lucide-react';
import { Button } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import { ProxyImage } from './ProxyImage';
import {
  type DayWithMemories,
  type FailedDay,
  hasMemories,
  useRecentDays,
} from './use-recent-days';

const item =
  'flex w-18 flex-col items-center gap-1 rounded-card text-small outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus';
const circle = 'flex size-16 items-center justify-center overflow-hidden rounded-full';

export function RecentDays({ today }: { today: string }) {
  const { t } = useTranslation();
  const { days, visible, earlierLabel, showEarlier } = useRecentDays(today);
  if (!visible) return null;
  return (
    <ul
      aria-label={t('onThisDay.recentDays')}
      className="-mx-4 flex snap-x gap-4 overflow-x-auto px-4 pb-2"
    >
      {days.map((day) => (
        <li key={day.date} className="shrink-0 snap-start">
          {hasMemories(day) ? <DayLink {...day} /> : <Retry {...day} />}
        </li>
      ))}
      <li className="shrink-0 snap-start">
        <Earlier label={earlierLabel} onPress={showEarlier} />
      </li>
    </ul>
  );
}

function DayLink({ date, label, newestYear, thumbnail }: DayWithMemories) {
  return (
    <Link
      to="/day/$date"
      params={{ date }}
      search={{ year: newestYear }}
      resetScroll={false}
      className={item}
    >
      <span className={`${circle} bg-surface-raised`}>
        <ProxyImage src={thumbnail} className="size-full object-cover" />
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

function Earlier({ label, onPress }: { label: ParseKeys; onPress: () => void }) {
  const { t } = useTranslation();
  return (
    <Button onPress={onPress} className={item}>
      <span className={`${circle} bg-surface-raised`}>
        <ChevronRight aria-hidden className="size-6" />
      </span>
      <span aria-live="polite">{t(label)}</span>
    </Button>
  );
}
