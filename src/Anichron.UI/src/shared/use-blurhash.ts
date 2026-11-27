import { useState } from 'react';
import type { MemoryMedia } from '../api/memories';
import { blurhashOf } from './blurhash';

export function useBlurhash(memory: MemoryMedia) {
  const [decoded, setDecoded] = useState(false);
  return {
    blurhash: decoded ? null : blurhashOf(memory),
    onDecoded: () => {
      setDecoded(true);
    },
  };
}
