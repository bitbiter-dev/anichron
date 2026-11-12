import { queryOptions, type QueryClient } from '@tanstack/react-query';
import type { ApiClient } from '../../api/client';
import type { WorkItem, WorkItemPage } from '../../api/work-items';
import { workItemsKey } from './route';

const pagesKey = [...workItemsKey, 'page'] as const;
const pageKey = (page: number) => [...pagesKey, page] as const;
const itemKey = (id: string) => [...workItemsKey, 'item', id] as const;

const instant = (text: string) => text.slice(0, -1);

const updatedSince = (item: WorkItem, other: WorkItem) =>
  instant(item.updatedAt) >= instant(other.updatedAt);

const requestedAfter = (item: WorkItem, other: WorkItem) =>
  instant(item.requestedAt) > instant(other.requestedAt);

const pagesOf = (queryClient: QueryClient) =>
  queryClient
    .getQueriesData<WorkItemPage>({ queryKey: pagesKey })
    .flatMap(([, page]) => page ?? []);

function freshest(queryClient: QueryClient, fetched: WorkItem) {
  return [
    ...pagesOf(queryClient).flatMap((page) => page.items),
    queryClient.getQueryData<WorkItem | null>(itemKey(fetched.id)),
  ].reduce<WorkItem>(
    (kept, item) => (item?.id === kept.id && !updatedSince(kept, item) ? item : kept),
    fetched,
  );
}

function inserted(page: WorkItemPage, item: WorkItem): WorkItemPage {
  const oldest = page.items.at(-1);
  if (oldest && !requestedAfter(item, oldest)) return page;
  const at = page.items.findIndex((other) => requestedAfter(item, other));
  return { ...page, items: page.items.toSpliced(at, 0, item), total: page.total + 1 };
}

export function merge(queryClient: QueryClient, item: WorkItem) {
  const known = pagesOf(queryClient).some((page) => page.items.some(({ id }) => id === item.id));
  queryClient.setQueriesData<WorkItemPage>(
    { queryKey: pagesKey },
    (page) =>
      page && {
        ...page,
        items: page.items.map((other) =>
          other.id === item.id && updatedSince(item, other) ? item : other,
        ),
      },
  );
  queryClient.setQueryData<WorkItem | null>(itemKey(item.id), (current) =>
    !current || updatedSince(item, current) ? item : current,
  );
  if (!known)
    queryClient.setQueryData<WorkItemPage>(pageKey(1), (page) => page && inserted(page, item));
}

export const pageQuery = (api: ApiClient, queryClient: QueryClient, page: number) =>
  queryOptions({
    queryKey: pageKey(page),
    queryFn: async () => {
      const fetched = await api.workItems.list(page);
      return { ...fetched, items: fetched.items.map((item) => freshest(queryClient, item)) };
    },
    staleTime: Infinity,
    refetchOnMount: page > 1 && 'always',
  });

export const itemQuery = (api: ApiClient, queryClient: QueryClient, id: string) =>
  queryOptions({
    queryKey: itemKey(id),
    queryFn: async () => {
      const fetched = await api.workItems.get(id);
      return fetched && freshest(queryClient, fetched);
    },
    staleTime: Infinity,
  });
