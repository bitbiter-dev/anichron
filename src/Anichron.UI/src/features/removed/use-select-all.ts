import type { InfiniteData, UseInfiniteQueryResult } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
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
  const pages = query.data?.pages ?? [];
  const remaining = Math.max((pages.at(-1)?.total ?? 0) - idsOf(query.data).length, 0);

  async function loadAll() {
    let result = await query.fetchNextPage({ cancelRefetch: false });
    while (result.hasNextPage && !result.isFetchNextPageError)
      result = await query.fetchNextPage({ cancelRefetch: false });
    if (selecting.current) select.selectAll(idsOf(result.data));
  }

  const deselects = !query.hasNextPage && select.holdsAll;
  return {
    deselects,
    press: () => {
      if (deselects) select.clear();
      else if (query.hasNextPage) setAsking(true);
      else select.selectAll();
    },
    asking: asking && { total: shown + remaining, remaining },
    cancel: () => {
      setAsking(false);
    },
    confirm: () => {
      setAsking(false);
      void loadAll();
    },
  };
}
