import { useInfiniteQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import { usePaging } from '../../shared/use-paging';
import { failureOf, removedQuery, rowOf } from './removed';
import { useDragSelect } from './use-drag-select';
import { useForget } from './use-forget';
import { useSelect } from './use-select';

export function useRemoved() {
  const { api } = useRouter().options.context;
  const query = useInfiniteQuery(removedQuery(api));
  const [opened, setOpened] = useState<number | null>(null);
  const [left, setLeft] = useState<ReadonlySet<string>>(() => new Set());
  const rows = (query.data?.pages.flatMap((page) => page.items) ?? [])
    .filter(({ id }) => !left.has(id))
    .map(rowOf);
  const select = useSelect(rows.map(({ memory }) => memory.id));
  const drag = useDragSelect(select);
  const forget = useForget((sent) => {
    if (rows.length === sent.length) setOpened(null);
    setLeft((ids) => new Set([...ids, ...sent]));
  }, select.cancel);
  const moreFailed = query.isFetchNextPageError && !query.isFetchingNextPage;
  const { nearing } = usePaging(() => {
    if (query.hasNextPage && !query.isFetchNextPageError)
      void query.fetchNextPage({ cancelRefetch: false });
  }, rows.length);

  return {
    rows,
    loading: query.isPending,
    empty: query.isSuccess && rows.length === 0,
    failure: failureOf(query.error, query.data !== undefined),
    retry: () => void query.refetch(),
    loadingMore: query.isFetchingNextPage,
    moreFailed,
    retryMore: () => void query.fetchNextPage(),
    opened,
    open: (index: number) => () => {
      setOpened(index);
    },
    close: () => {
      setOpened(null);
    },
    nearing,
    select,
    drag,
    forget,
    press: (index: number, range: boolean) => {
      if (drag.claimed()) return;
      if (select.selecting) select.toggle(index, range);
      else setOpened(index);
    },
    hold: (index: number) => {
      if (!select.selecting) drag.hold(index);
    },
  };
}

export type RemovedScreen = ReturnType<typeof useRemoved>;
