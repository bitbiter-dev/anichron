import { useQuery } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { dayQuery } from './day';
import { dayView, thumbnailOf } from './day-view';
import { useGroupsShown } from './use-groups-shown';

export function useDayView(date: string) {
  const { api } = useRouter().options.context;
  const day = useQuery(dayQuery(api, date));
  const view = dayView(day);
  const shown = useGroupsShown([
    { settled: !day.isPending, pictures: (view.years ?? []).map(thumbnailOf) },
  ]);
  const loading = shown === 0;
  return { view: loading ? {} : view, loading, retry: () => void day.refetch() };
}
