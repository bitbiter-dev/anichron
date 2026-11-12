import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getRouteApi, useRouter } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import type { WorkItem } from '../../api/work-items';
import { itemQuery } from './cache';
import { Failed } from './Failed';
import { Cancel, Progress } from './parts';
import { at, cancellable, failureOf, resultFields, statusOf, timestamps } from './view';

const detail = getRouteApi('/signed-in/shell/admin/admin/work-items/$id');

export function WorkItemPage() {
  const { t } = useTranslation();
  const { id } = detail.useParams();
  const { api } = useRouter().options.context;
  const query = useQuery(itemQuery(api, useQueryClient(), id));
  const failure = failureOf(query);
  return (
    <>
      {query.data && <Details item={query.data} />}
      {query.data === null && <p>{t('workItems.notFound')}</p>}
      {failure && <Failed message={failure} onRetry={() => void query.refetch()} />}
    </>
  );
}

const term = 'text-small text-fg-muted';

function Details({ item }: { item: WorkItem }) {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-4 rounded-card p-4 glass-card">
      <h2 className="font-semibold">{t(`workItems.kinds.${item.kind}`)}</h2>
      <Progress item={item} />
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2">
        <dt className={term}>{t('workItems.fields.status')}</dt>
        <dd>{t(statusOf(item))}</dd>
        {timestamps(item).map(({ field, value }) => (
          <div key={field} className="contents">
            <dt className={term}>{t(`workItems.fields.${field}`)}</dt>
            <dd>{t('workItems.at', at(value))}</dd>
          </div>
        ))}
        <dt className={term}>{t('workItems.fields.attempt')}</dt>
        <dd>{item.attempt}</dd>
        <dt className={term}>{t('workItems.fields.stalls')}</dt>
        <dd>{item.stalls}</dd>
      </dl>
      <Result item={item} />
      {item.error && <ErrorText message={item.error} />}
      {cancellable(item) && <Cancel item={item} />}
    </section>
  );
}

function Result({ item }: { item: WorkItem }) {
  const { t } = useTranslation();
  const fields = resultFields(item.result);
  if (fields.length === 0) return null;
  return (
    <section className="flex flex-col gap-2">
      <h3 className="font-semibold">
        {t(item.resultIsPartial ? 'workItems.fields.partialResult' : 'workItems.fields.result')}
      </h3>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-small">
        {fields.map(({ name, value }) => (
          <div key={name} className="contents">
            <dt className="text-fg-muted">{name}</dt>
            <dd className="break-all">{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function ErrorText({ message }: { message: string }) {
  const { t } = useTranslation();
  return (
    <section className="flex flex-col gap-2">
      <h3 className="font-semibold">{t('workItems.fields.error')}</h3>
      <pre className="text-small break-all whitespace-pre-wrap">{message}</pre>
    </section>
  );
}
