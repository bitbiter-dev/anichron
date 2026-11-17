import type { Memory } from '../../api/flashbacks';
import type { Place, StoryState } from './story';
import { kindOf } from './story-view';

type MemoryAt = (place: Place) => Memory;

function upcomingOf({ load, upcoming }: StoryState, memoryAt: MemoryAt) {
  return load === 'loading' ? [] : upcoming.map(memoryAt);
}

export function createPrefetch(
  memoryAt: MemoryAt,
  prefetchPicture: (url: string) => Promise<boolean>,
) {
  const fetched = new Set<string>();
  const decoded = new Set<string>();
  return {
    fetchAhead(state: StoryState) {
      for (const { proxies } of upcomingOf(state, memoryAt)) {
        const url = proxies.preview;
        if (url && !fetched.has(url)) {
          fetched.add(url);
          void prefetchPicture(url).then((done) => {
            if (done) decoded.add(url);
          });
        }
      }
    },
    decoded: (url: string | null) => url !== null && decoded.has(url),
  };
}

export function createKeepLiveVideos(memoryAt: MemoryAt, keep: (urls: string[]) => void) {
  return (state: StoryState) => {
    if (state.load === 'loading') return;
    keep(
      [state, ...state.upcoming, ...state.behind]
        .map(memoryAt)
        .flatMap(({ livePhotoVideoUrl }) => livePhotoVideoUrl ?? []),
    );
  };
}

export function nextVideo({ upcoming }: StoryState, memoryAt: MemoryAt) {
  const video = upcoming.map(memoryAt).find((memory) => kindOf(memory) === 'video');
  return video?.proxies.video ?? null;
}
