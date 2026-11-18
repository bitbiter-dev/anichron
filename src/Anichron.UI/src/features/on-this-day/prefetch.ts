import type { Memory } from '../../api/flashbacks';
import type { Place, StoryState } from './story';
import { kindOf } from './story-view';

type MemoryAt = (place: Place) => Memory;

export function createPrefetch(
  previewAt: (place: Place) => string | null,
  prefetchPicture: (url: string) => Promise<boolean>,
) {
  const fetched = new Set<string>();
  const decoded = new Set<string | null>();
  return {
    fetchAhead({ load, upcoming }: StoryState) {
      if (load === 'loading') return;
      for (const url of upcoming.map(previewAt)) {
        if (url && !fetched.has(url)) {
          fetched.add(url);
          void prefetchPicture(url).then((done) => {
            if (done) decoded.add(url);
          });
        }
      }
    },
    decoded: (place: Place) => decoded.has(previewAt(place)),
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
