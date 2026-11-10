import { useEffect, useState } from 'react';

const secondsUntil = (deadline: number) => Math.ceil((deadline - Date.now()) / 1000);

export function useCountdown(initialSeconds = 0) {
  const [deadline, setDeadline] = useState<number | undefined>(
    () => Date.now() + initialSeconds * 1000,
  );
  const [remaining, setRemaining] = useState(initialSeconds);

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
