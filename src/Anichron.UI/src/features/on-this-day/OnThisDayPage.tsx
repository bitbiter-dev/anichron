import { Link, Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { yearsAgo } from './day';
import { type DayView, thumbnailOf } from './day-view';
import { useDayView } from './use-day-view';
import { Failed } from '../../shared/ui/Failed';
import { Skeleton } from '../../shared/ui/Skeleton';
import { ProxyImage } from './ProxyImage';
import { RecentDays } from './RecentDays';
import { Ring } from './Ring';
import { useOpening } from './use-opening';
import { useToday } from './use-today';

export function OnThisDayPage() {
  const { t } = useTranslation();
  const today = useToday();
  return (
    <>
      <main className="flex flex-col gap-4 p-4 pt-[calc(env(safe-area-inset-top)+1.5rem)]">
        <h1 className="text-title font-semibold">{t('onThisDay.title')}</h1>
        <Today key={today} date={today} />
        <RecentDays key={today} today={today} />
      </main>
      <Outlet />
    </>
  );
}

function Today({ date }: { date: string }) {
  const { view, loading, retry } = useDayView(date);
  return (
    <div
      aria-busy={loading}
      className="flex min-h-[calc(var(--spacing)*32*4/3+var(--spacing)*4+var(--text-small--line-height))] flex-col justify-center"
    >
      {loading && <GallerySkeleton />}
      <Day date={date} view={view} retry={retry} />
    </div>
  );
}

function Day({ date, view, retry }: { date: string; view: DayView; retry: () => void }) {
  const { t } = useTranslation();
  return (
    <>
      {view.years && <Gallery date={date} years={view.years} />}
      {view.empty && <p className="text-fg-muted">{t('onThisDay.nothing')}</p>}
      {view.failure && <Failed message={view.failure} onRetry={retry} />}
    </>
  );
}

function GallerySkeleton() {
  return (
    <div aria-hidden className="-mx-4 flex gap-3 overflow-hidden px-4 pb-2">
      {[0, 1, 2].map((card) => (
        <div key={card} className="flex shrink-0 flex-col gap-2">
          <Skeleton className="aspect-[3/4] w-32 rounded-card" />
          <div className="flex h-5 items-center">
            <Skeleton className="h-3 w-24 rounded-full" />
          </div>
        </div>
      ))}
    </div>
  );
}

function Gallery({ date, years }: { date: string; years: YearGroup[] }) {
  return (
    <ul className="-mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-2">
      {years.map((group) => (
        <li key={group.year} className="shrink-0 snap-start">
          <YearGroupLink date={date} group={group} />
        </li>
      ))}
    </ul>
  );
}

function YearGroupLink({ date, group }: { date: string; group: YearGroup }) {
  const { t } = useTranslation();
  return (
    <Link
      to="/day/$date"
      params={{ date }}
      search={{ year: group.year }}
      resetScroll={false}
      aria-busy={useOpening(date, group.year)}
      className="group flex min-w-32 flex-col gap-2 rounded-card text-small whitespace-nowrap outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
    >
      <div className="relative aspect-[3/4] w-32 overflow-hidden rounded-card bg-surface-raised outline-accent group-aria-busy:outline-2 group-aria-busy:-outline-offset-2">
        <ProxyImage src={thumbnailOf(group)} className="size-full object-cover" />
        <Ring className="inset-0 m-auto hidden size-8 group-aria-busy:block" />
      </div>
      {t('onThisDay.yearsAgo', yearsAgo(date, group.year))}
    </Link>
  );
}
