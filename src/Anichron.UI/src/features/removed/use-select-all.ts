import type { InfiniteData, UseInfiniteQueryResult } from '@tanstack/react-query';
import { type KeyboardEvent, useEffect, useRef, useState } from 'react';
import type { RemovedPage } from '../../api/soft-deleted';
import type { useSelect } from './use-select';

type RemovedQuery = UseInfiniteQueryResult<InfiniteData<RemovedPage>>;

function idsOf(data: InfiniteData<RemovedPage> | undefined) {
  return data?.pages.flatMap((page) => page.items.map(({ id }) => id)) ?? [];
}

export function useSelectAll(
  query: RemovedQuery,
  select: ReturnType<typeof useSelect>,
  shown: number,
) {
  const [asking, setAsking] = useState(false);
  const selecting = useRef(select.selecting);
  useEffect(() => {
    selecting.current = select.selecting;
  });
  const total = query.data?.pages.at(-1)?.total ?? 0;

  async function loadAll() {
    let result = await query.fetchNextPage({ cancelRefetch: false });
    while (result.hasNextPage && !result.isFetchNextPageError)
      result = await query.fetchNextPage({ cancelRefetch: false });
    if (selecting.current) select.selectAll(idsOf(result.data));
  }

  function all() {
    if (query.hasNextPage) setAsking(true);
    else select.selectAll();
  }

  const deselects = !query.hasNextPage && select.holdsAll;
  return {
    deselects,
    press: () => {
      if (deselects) select.clear();
      else all();
    },
    key: (event: KeyboardEvent) => {
      if (!select.selecting || event.key !== 'a' || !(event.ctrlKey || event.metaKey)) return;
      event.preventDefault();
      all();
    },
    asking: asking && { total, remaining: Math.max(total - shown, 0) },
    cancel: () => {
      setAsking(false);
    },
    confirm: () => {
      setAsking(false);
      void loadAll();
    },
  };
}
