import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Outlet, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { pageQuery } from './cache';
import { useStream } from './use-stream';

export function WorkItemsPage() {
  const { t } = useTranslation();
  const { api } = useRouter().options.context;
  const firstPage = useQuery(pageQuery(api, useQueryClient(), 1));
  const { reconnecting, gone } = useStream(firstPage.data?.cursor);
  return (
    <main className="flex flex-col gap-4 p-4">
      <p role="status" className="text-small text-fg-muted empty:hidden">
        {reconnecting && t('workItems.reconnecting')}
      </p>
      {gone ? <Gone /> : <Outlet />}
    </main>
  );
}

function Gone() {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-2">
      <h2 className="font-semibold">{t('notFound.title')}</h2>
      <p className="text-fg-muted">{t('notFound.body')}</p>
    </section>
  );
}
