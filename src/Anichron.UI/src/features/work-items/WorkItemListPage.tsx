import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, Link, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { WorkItem, WorkItemPage } from '../../api/work-items';
import { buttonStyles } from '../../shared/ui/Button';
import { pageQuery } from './cache';
import { Failed } from '../../shared/ui/Failed';
import { Skeleton } from '../../shared/ui/Skeleton';
import { Cancel, Progress } from './parts';
import { at, cancellable, failureOf, pagesAround, statusOf } from './view';

const list = getRouteApi('/signed-in/shell/admin/admin/work-items/');

export function WorkItemListPage() {
  const { page = 1 } = list.useSearch();
  return <ListPage key={page} page={page} />;
}

function ListPage({ page }: { page: number }) {
  const { api } = useRouter().options.context;
  const query = useQuery(pageQuery(api, useQueryClient(), page));
  const failure = failureOf(query);
  return (
    <div aria-busy={query.isPending} className="flex flex-col gap-4">
      {query.isPending && <ItemsSkeleton />}
      {query.data && <Items page={query.data} />}
      {failure && <Failed message={failure} onRetry={() => void query.refetch()} />}
    </div>
  );
}

function ItemsSkeleton() {
  return (
    <div aria-hidden className="flex flex-col gap-3">
      {[0, 1, 2].map((row) => (
        <div key={row} className="flex flex-col gap-2 rounded-card p-4 glass-card">
          <div className="flex h-6 items-center justify-between gap-3">
            <Skeleton className="h-4 w-28 rounded-full" />
            <Skeleton className="h-3 w-16 rounded-full" />
          </div>
          <Skeleton className="h-1.5 w-full rounded-full" />
          <div className="flex h-5 items-center">
            <Skeleton className="h-3 w-44 rounded-full" />
          </div>
        </div>
      ))}
    </div>
  );
}

function Items({ page }: { page: WorkItemPage }) {
  const { t } = useTranslation();
  if (page.items.length === 0) return <p className="text-fg-muted">{t('workItems.empty')}</p>;
  return (
    <>
      <ul className="flex flex-col gap-3">
        {page.items.map((item) => (
          <Row key={item.id} item={item} />
        ))}
      </ul>
      <Pages page={page} />
    </>
  );
}

function Row({ item }: { item: WorkItem }) {
  const { t } = useTranslation();
  return (
    <li className="flex flex-col gap-2 rounded-card p-4 glass-card">
      <div className="flex items-baseline justify-between gap-3">
        <Link
          to="/admin/work-items/$id"
          params={{ id: item.id }}
          className="font-semibold text-accent outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
        >
          {t(`workItems.kinds.${item.kind}`)}
        </Link>
        <span className="text-small">{t(statusOf(item))}</span>
      </div>
      <Progress item={item} />
      <p className="text-small text-fg-muted">{t('workItems.requested', at(item.requestedAt))}</p>
      {cancellable(item) && <Cancel item={item} />}
    </li>
  );
}

function Pages({ page }: { page: WorkItemPage }) {
  const { t } = useTranslation();
  const { newer, older } = pagesAround(page);
  return (
    <nav aria-label={t('workItems.pages')} className="flex justify-between">
      {newer && (
        <Link to="/admin/work-items" search={{ page: newer }} className={buttonStyles()}>
          {t('workItems.newer')}
        </Link>
      )}
      {older && (
        <Link
          to="/admin/work-items"
          search={{ page: older }}
          className={buttonStyles({ class: 'ml-auto' })}
        >
          {t('workItems.older')}
        </Link>
      )}
    </nav>
  );
}
