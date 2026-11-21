import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { listen } from '../../shared/listen';
import { localDate } from './day';

export function useToday() {
  const [today, setToday] = useState(localDate);
  const navigate = useNavigate();
  useEffect(() =>
    listen(document, 'visibilitychange', () => {
      const now = localDate();
      if (document.visibilityState !== 'visible' || now === today) return;
      void navigate({
        // Stryker disable next-line StringLiteral
        to: '.',
        search: (current) => ({ ...current, earlier: undefined }),
        replace: true,
      }).then(() => {
        setToday(now);
      });
    }),
  );
  return today;
}
