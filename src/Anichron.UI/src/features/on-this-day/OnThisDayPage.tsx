import { Link, Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { cardKey, type DayView } from './day-view';
import { useDayView } from './use-day-view';
import { Failed } from '../../shared/ui/Failed';
import { Skeleton } from '../../shared/ui/Skeleton';
import { ProxyImage } from '../../shared/ui/ProxyImage';
import { RecentDays } from './RecentDays';
import { Ring } from '../../shared/ui/Ring';
import { useOpening } from './use-opening';
import { useOnThisDay } from './use-on-this-day';
import { useRecentDays } from './use-recent-days';
import { useTodayCard } from './use-today-card';

export function OnThisDayPage() {
  const { t } = useTranslation();
  const page = useOnThisDay();
  return (
    <>
      <main className="flex flex-col gap-4 p-4 pt-[calc(env(safe-area-inset-top)+1.5rem)]">
        <h1 className="text-title font-semibold">{t('onThisDay.title')}</h1>
        {page.shows === 'day' ? (
          <DayScreen key={page.today} today={page.today} />
        ) : (
          <NoPhotos hint={page.hint} />
        )}
      </main>
      <Outlet />
    </>
  );
}

function NoPhotos({ hint }: { hint: boolean }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-1">
      <p className="font-semibold">{t('onThisDay.noPhotos')}</p>
      {hint && <p className="text-small text-fg-muted">{t('onThisDay.noPhotosHint')}</p>}
    </div>
  );
}

function DayScreen({ today }: { today: string }) {
  const tray = useRecentDays(today);
  return (
    <>
      <Today date={today} firstScreen={!tray.loading} />
      <RecentDays tray={tray} />
    </>
  );
}

interface TodayProps {
  date: string;
  firstScreen: boolean;
}

function Today({ date, firstScreen }: TodayProps) {
  const { view, loading, retry } = useDayView(date);
  return (
    <div
      aria-busy={loading}
      className="flex min-h-[calc(var(--spacing)*40*4/3+var(--spacing)*2+var(--text-small--line-height))] flex-col justify-center"
    >
      {loading && <CardSkeleton />}
      <Day date={date} view={view} retry={retry} firstScreen={firstScreen && !loading} />
    </div>
  );
}

interface DayProps extends TodayProps {
  view: DayView;
  retry: () => void;
}

function Day({ date, view, retry, firstScreen }: DayProps) {
  const { t } = useTranslation();
  return (
    <>
      {view.years && (
        <TodayCard
          key={cardKey(view.years)}
          date={date}
          years={view.years}
          firstScreen={firstScreen}
        />
      )}
      {view.empty && <p className="text-fg-muted">{t('onThisDay.nothing')}</p>}
      {view.failure && <Failed message={view.failure} onRetry={retry} />}
    </>
  );
}

function CardSkeleton() {
  return (
    <div aria-hidden className="flex flex-col gap-2">
      <Skeleton className="aspect-[3/4] w-40 rounded-card" />
      <div className="flex h-5 items-center">
        <Skeleton className="h-3 w-24 rounded-full" />
      </div>
    </div>
  );
}

interface TodayCardProps extends TodayProps {
  years: YearGroup[];
}

function TodayCard({ date, years, firstScreen }: TodayCardProps) {
  const card = useTodayCard(date, years, firstScreen);
  return (
    <Link
      to="/day/$date"
      params={{ date }}
      search={{ year: card.newestYear }}
      resetScroll={false}
      aria-busy={useOpening(date, card.newestYear)}
      {...card.focusProps}
      className="group flex w-40 flex-col gap-2 self-start rounded-card text-small whitespace-nowrap outline-none focus-visible:focus-ring"
    >
      <div className="relative aspect-[3/4] w-40 overflow-hidden rounded-card bg-surface-raised outline-accent group-aria-busy:outline-2 group-aria-busy:-outline-offset-2">
        {card.pictures.map(({ key, source, shown }) => (
          <span
            key={key}
            data-shown={shown}
            className="absolute inset-0 opacity-0 transition-opacity duration-(--duration-crossfade) data-shown:opacity-100"
          >
            <ProxyImage src={source} className="size-full object-cover" />
          </span>
        ))}
        <Ring className="inset-0 m-auto hidden size-8 group-aria-busy:block" />
      </div>
      {card.label}
    </Link>
  );
}
