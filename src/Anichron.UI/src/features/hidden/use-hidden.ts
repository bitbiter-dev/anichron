import { useInfiniteQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import type { HiddenMemory } from '../../api/hidden';
import { burstsKey, daysKey } from '../../shared/memory-queries';
import { usePaging } from '../../shared/use-paging';
import { failureOf, hiddenQuery, sectionsOf } from './hidden';

function without(ids: ReadonlySet<string>, id: string) {
  const left = new Set(ids);
  left.delete(id);
  return left;
}

export function useHidden() {
  const { api, queryClient, toasts } = useRouter().options.context;
  const query = useInfiniteQuery(hiddenQuery(api));
  const [unhidden, setUnhidden] = useState<ReadonlySet<string>>(() => new Set());
  const [opened, setOpened] = useState<number | null>(null);
  const items = (query.data?.pages.flatMap((page) => page.items) ?? []).filter(
    ({ memory }) => !unhidden.has(memory.id),
  );

  const { nearing } = usePaging(() => {
    if (query.hasNextPage) void query.fetchNextPage({ cancelRefetch: false });
  }, items.length);

  function send(id: string, hidden: boolean, rollBack: () => void) {
    api.assets.interact(id, { hidden }).then(
      () => {
        void queryClient.invalidateQueries({ queryKey: daysKey() });
        void queryClient.invalidateQueries({ queryKey: burstsKey() });
      },
      () => {
        rollBack();
        toasts.show({ message: 'story.notSaved' });
      },
    );
  }

  function hideAgain({ memory }: HiddenMemory) {
    setUnhidden((ids) => without(ids, memory.id));
    send(memory.id, true, () => {
      setUnhidden((ids) => new Set(ids).add(memory.id));
    });
  }

  function unhide(item: HiddenMemory) {
    const { id } = item.memory;
    if (items.length === 1) setOpened(null);
    setUnhidden((ids) => new Set(ids).add(id));
    const key = toasts.show({
      message: 'hidden.unhidden',
      action: {
        label: 'story.undo',
        onAction: () => {
          hideAgain(item);
        },
      },
    });
    send(id, false, () => {
      toasts.close(key);
      setUnhidden((ids) => without(ids, id));
    });
  }

  return {
    items,
    sections: sectionsOf(items, new Date()),
    loading: query.isPending,
    empty: query.isSuccess && items.length === 0,
    failure: failureOf(query.error),
    retry: () => void query.refetch(),
    loadingMore: query.isFetchingNextPage,
    opened,
    open: (index: number) => () => {
      setOpened(index);
    },
    close: () => {
      setOpened(null);
    },
    unhide,
    nearing,
  };
}

export type HiddenScreen = ReturnType<typeof useHidden>;
