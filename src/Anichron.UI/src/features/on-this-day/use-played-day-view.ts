import type { UseQueryResult } from '@tanstack/react-query';
import { useState } from 'react';
import type { Day } from '../../api/flashbacks';
import { type DayView, dayView } from './day-view';

export function usePlayedDayView(day: UseQueryResult<Day>) {
  const view = dayView(day);
  const [played, setPlayed] = useState<DayView | null>(null);
  if (!played && view.years) setPlayed(view);
  return played ?? view;
}
