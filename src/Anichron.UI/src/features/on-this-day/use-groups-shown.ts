import { useQueries } from '@tanstack/react-query';
import { useRouter } from '@tanstack/react-router';
import { useState } from 'react';
import { pictureQuery } from './day';

export interface PictureGroup {
  settled: boolean;
  pictures: readonly (string | null)[];
}

export function useGroupsShown(groups: readonly PictureGroup[]) {
  const { api } = useRouter().options.context;
  const wanted = [...new Set(groups.flatMap(({ pictures }) => pictures))].filter(
    (url) => url !== null,
  );
  const results = useQueries({ queries: wanted.map((url) => pictureQuery(api, url)) });
  const done = new Set(wanted.filter((_, index) => results[index]?.isSuccess));
  const ready = groups.map(
    ({ settled, pictures }) => settled && pictures.every((url) => url === null || done.has(url)),
  );
  const readyNow = ready.includes(false) ? ready.indexOf(false) : groups.length;
  const [shown, setShown] = useState(0);
  if (readyNow > shown) setShown(readyNow);
  return Math.max(readyNow, shown);
}
