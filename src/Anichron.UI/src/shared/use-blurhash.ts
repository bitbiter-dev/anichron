import { useState } from 'react';
import type { Memory } from '../api/flashbacks';
import { blurhashOf } from './blurhash';

export function useBlurhash(memory: Memory) {
  const [decoded, setDecoded] = useState(false);
  return {
    blurhash: decoded ? null : blurhashOf(memory),
    onDecoded: () => {
      setDecoded(true);
    },
  };
}
