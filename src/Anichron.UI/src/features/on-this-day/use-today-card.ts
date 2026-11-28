import { useMatch, useRouter } from '@tanstack/react-router';
import { useEffect, useState, useSyncExternalStore } from 'react';
import { useFocusRing } from 'react-aria';
import { useTranslation } from 'react-i18next';
import type { YearGroup } from '../../api/flashbacks';
import { isVisible, onVisibilityChange } from '../../shared/listen';
import { reducesMotion } from '../../shared/reduced-motion';
import { dayLabel } from './day';
import { thumbnailOf } from './day-view';
import { createTurning } from './turning';

export function useTodayCard(date: string, years: readonly YearGroup[], firstScreen: boolean) {
  const { t } = useTranslation();
  const { downloads } = useRouter().options.context.api.media;
  const [others] = useState(() =>
    years
      .slice(1)
      .map(thumbnailOf)
      .filter((url) => url !== null),
  );
  const [turning] = useState(() => createTurning(others, downloads));
  const leaf =
    useMatch({ from: '/signed-in/shell/on-this-day/', shouldThrow: false }) !== undefined;
  const handsOver = leaf && firstScreen;
  useEffect(() => {
    if (handsOver) downloads.keep(others.map((url) => ({ url, kind: 'picture' })));
  }, [handsOver, downloads, others]);
  const ring = useFocusRing();
  const visible = useSyncExternalStore(onVisibilityChange, isVisible);
  const still = !leaf || ring.isFocusVisible || !visible || reducesMotion();
  useEffect(() => {
    turning.standStill(still);
  }, [turning, still]);
  const shown = useSyncExternalStore(turning.subscribe, turning.shown);
  const { sourceOf } = useSyncExternalStore(downloads.subscribe, downloads.snapshot);
  const newest = years[0] as YearGroup;
  return {
    label: dayLabel(t, date).replace('\n', ' '),
    newestYear: newest.year,
    focusProps: ring.focusProps,
    pictures: [thumbnailOf(newest), ...others.map(sourceOf)].map((source, place) => ({
      key: place,
      source,
      shown: place === shown || undefined,
    })),
  };
}
