import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Button as AriaButton } from 'react-aria-components';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { Button } from '../../shared/ui/Button';
import { thumbnailOf } from './day-view';
import { ProxyImage } from './ProxyImage';
import { type RecentDay, useRecentDays } from './use-recent-days';

export function RecentDays({ today }: { today: string }) {
  const { t } = useTranslation();
  const { days, shown, nothing, earlier } = useRecentDays(today);
  if (!shown) return null;
  return (
    <ul
      aria-label={t('onThisDay.recentDays')}
      className="-mx-4 flex snap-x gap-4 overflow-x-auto px-4 pb-2"
    >
      {days.map((day) => (
        <li key={day.date} className="shrink-0 snap-start">
          {day.years ? (
            <Circle date={day.date} label={day.label} years={day.years} />
          ) : (
            <Retry label={day.label} onRetry={day.retry} />
          )}
        </li>
      ))}
      <li className="shrink-0 snap-start">
        <Earlier nothing={nothing} onPress={earlier} />
      </li>
    </ul>
  );
}

interface CircleProps {
  date: string;
  label: string;
  years: YearGroup[];
}

function Circle({ date, label, years }: CircleProps) {
  const newest = years[0] as YearGroup;
  return (
    <Link
      to="/day/$date"
      params={{ date }}
      search={{ year: newest.year }}
      resetScroll={false}
      className="flex w-18 flex-col items-center gap-1 rounded-card text-small outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <div className="size-16 overflow-hidden rounded-full bg-surface-raised">
        <ProxyImage src={thumbnailOf(newest)} className="size-full object-cover" />
      </div>
      {label}
    </Link>
  );
}

function Retry({ label, onRetry }: { label: string; onRetry: RecentDay['retry'] }) {
  const { t } = useTranslation();
  return (
    <div className="flex w-18 flex-col items-center gap-1 text-small">
      <Button onPress={onRetry} className="size-16 rounded-full px-0 text-small">
        {t('onThisDay.retry')}
      </Button>
      {label}
    </div>
  );
}

function Earlier({ nothing, onPress }: { nothing: boolean; onPress: () => void }) {
  const { t } = useTranslation();
  return (
    <AriaButton
      onPress={onPress}
      className="flex w-18 flex-col items-center gap-1 rounded-card text-small outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <span className="flex size-16 items-center justify-center rounded-full bg-surface-raised">
        <ChevronRight aria-hidden className="size-6" />
      </span>
      <span aria-live="polite">
        {t(nothing ? 'onThisDay.nothingEarlier' : 'onThisDay.earlier')}
      </span>
    </AriaButton>
  );
}
