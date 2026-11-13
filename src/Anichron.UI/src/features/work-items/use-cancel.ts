import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { WorkItem } from '../../api/work-items';
import { merge } from './cache';

export function useCancel(item: WorkItem) {
  const { api } = useRouter().options.context;
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.workItems.cancel(item.id),
    onSuccess: (cancelled) => {
      merge(queryClient, cancelled);
    },
  });
}
