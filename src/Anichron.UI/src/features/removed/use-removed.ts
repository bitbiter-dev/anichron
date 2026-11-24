import { useInfiniteQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import { usePaging } from '../../shared/use-paging';
import { failureOf, removedQuery, rowOf } from './removed';

export function useRemoved() {
  const { api } = useRouter().options.context;
  const query = useInfiniteQuery(removedQuery(api));
  const [opened, setOpened] = useState<number | null>(null);
  const rows = (query.data?.pages.flatMap((page) => page.items) ?? []).map(rowOf);
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
  };
}

export type RemovedScreen = ReturnType<typeof useRemoved>;
