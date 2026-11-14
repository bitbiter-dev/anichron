import type { Memory } from '../../api/flashbacks';
import type { Place, StoryState } from './story';
import { kindOf } from './story-view';

type MemoryAt = (place: Place) => Memory;

function upcomingOf({ load, upcoming }: StoryState, memoryAt: MemoryAt) {
  return load === 'loading' ? [] : upcoming.map(memoryAt);
}

export function createPrefetch(memoryAt: MemoryAt, prefetchPicture: (url: string) => void) {
  const fetched = new Set<string>();
  return (state: StoryState) => {
    for (const { proxies } of upcomingOf(state, memoryAt)) {
      const url = proxies.preview;
      if (url && !fetched.has(url)) {
        fetched.add(url);
        prefetchPicture(url);
      }
    }
  };
}

export function nextVideo({ upcoming }: StoryState, memoryAt: MemoryAt) {
  const video = upcoming.map(memoryAt).find((memory) => kindOf(memory) === 'video');
  return video?.proxies.video ?? null;
}
