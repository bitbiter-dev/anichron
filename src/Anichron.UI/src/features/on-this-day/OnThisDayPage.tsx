import { useQuery } from '@tanstack/react-query';
import { Link, Outlet, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { dayQuery, yearsAgo } from './day';
import { dayView, thumbnailOf } from './day-view';
import { Failed } from './Failed';
import { ProxyImage } from './ProxyImage';
import { RecentDays } from './RecentDays';
import { useToday } from './use-today';

export function OnThisDayPage() {
  const { t } = useTranslation();
  const today = useToday();
  return (
    <>
      <main className="flex flex-col gap-4 p-4">
        <h1 className="text-title font-semibold">{t('onThisDay.title')}</h1>
        <Today date={today} />
        <RecentDays today={today} />
      </main>
      <Outlet />
    </>
  );
}

function Today({ date }: { date: string }) {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const day = useQuery(dayQuery(api, date));
  const state = dayView(day);
  return (
    <>
      {state.years && <Gallery date={date} years={state.years} />}
      {state.empty && <p className="text-fg-muted">{t('onThisDay.nothing')}</p>}
      {state.failure && <Failed message={state.failure} onRetry={() => void day.refetch()} />}
    </>
  );
}

function Gallery({ date, years }: { date: string; years: YearGroup[] }) {
  const { t } = useTranslation();
  return (
    <ul className="-mx-4 flex snap-x gap-3 overflow-x-auto px-4 pb-2">
      {years.map((group) => (
        <li key={group.year} className="shrink-0 snap-start">
          <Link
            to="/day/$date"
            params={{ date }}
            search={{ year: group.year }}
            resetScroll={false}
            className="flex w-32 flex-col gap-2 rounded-card text-small outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          >
            <div className="aspect-[3/4] overflow-hidden rounded-card bg-surface-raised">
              <ProxyImage src={thumbnailOf(group)} className="size-full object-cover" />
            </div>
            {t('onThisDay.yearsAgo', yearsAgo(date, group.year))}
          </Link>
        </li>
      ))}
    </ul>
  );
}
