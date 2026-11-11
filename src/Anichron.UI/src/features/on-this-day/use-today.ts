import { useEffect, useState } from 'react';
import { listen } from '../../shared/listen';
import { localDate } from './day';

export function useToday() {
  const [today, setToday] = useState(localDate);
  useEffect(() =>
    listen(document, 'visibilitychange', () => {
      if (document.visibilityState === 'visible') setToday(localDate());
    }),
  );
  return today;
}
