import { useEffect, useState } from 'react';

function secondsUntil(deadline: number) {
  return Math.ceil((deadline - Date.now()) / 1000);
}

export function useCountdown() {
  const [deadline, setDeadline] = useState<number>();
  const [remaining, setRemaining] = useState(0);

  useEffect(() => {
    if (deadline === undefined) return undefined;
    const timer = setInterval(() => {
      const left = Math.max(0, secondsUntil(deadline));
      setRemaining(left);
      if (left === 0) setDeadline(undefined);
    }, 1000);
    return () => {
      clearInterval(timer);
    };
  }, [deadline]);

  return {
    remaining,
    start(seconds: number) {
      setDeadline(Date.now() + seconds * 1000);
      setRemaining(seconds);
    },
  };
}
