import { queryOptions, useQueries } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import type { ApiClient } from '../../api/client';

function pictureQuery(api: ApiClient, url: string) {
  return queryOptions({
    queryKey: ['picture', url],
    queryFn: () => api.media.loadPicture(url),
    staleTime: Infinity,
  });
}

export function useLoadedPictures(urls: readonly (string | null)[]) {
  const { api } = useRouter().options.context;
  const wanted = [...new Set(urls)].filter((url) => url !== null);
  const pictures = useQueries({ queries: wanted.map((url) => pictureQuery(api, url)) });
  const loaded = new Set(wanted.filter((_, index) => pictures[index]?.isSuccess));
  return (group: readonly (string | null)[]) =>
    group.every((url) => url === null || loaded.has(url));
}
