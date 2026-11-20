import type { Memory } from '../../api/flashbacks';
import type { Downloads, Wanted } from '../../api/media';
import type { Place, StoryState } from './story';
import { kindOf } from './story-view';

type MemoryAt = (place: Place) => Memory;
type PreviewAt = (place: Place) => string | null;

export function wantedOf(state: StoryState, previewAt: PreviewAt, memoryAt: MemoryAt): Wanted {
  const pictures = (places: readonly Place[]) =>
    places
      .flatMap((place) => previewAt(place) ?? [])
      .map((url) => ({ url, kind: 'picture' as const }));
  const videoOf = (place: Place) => memoryAt(place).livePhotoVideoUrl;
  const videos = (urls: readonly (string | null)[]) =>
    urls.flatMap((url) => (url ? [{ url, kind: 'video' as const }] : []));
  const current = videoOf(state);
  return [
    ...pictures([state]),
    ...videos(state.livePlay ? [current] : []),
    ...pictures(state.upcoming),
    ...videos([current, state.upcoming.map(videoOf).find(Boolean) ?? null]),
  ];
}

export function createPreviews(downloads: Downloads, previewAt: PreviewAt) {
  let visit = 0;
  return {
    downloaded: (place: Place) => downloads.sourceOf(previewAt(place)) !== null,
    sync(state: StoryState, failed: (visit: number) => void) {
      const url = previewAt(state);
      if (state.visit !== visit) downloads.retry(url);
      visit = state.visit;
      if (state.load === 'loading' && state.kind !== 'video' && downloads.failed(url))
        failed(state.visit);
    },
  };
}

export function nextVideo({ upcoming }: StoryState, memoryAt: MemoryAt) {
  const video = upcoming.map(memoryAt).find((memory) => kindOf(memory) === 'video');
  return video?.proxies.video ?? null;
}

export function createPreload() {
  let preloading: string | null = null;
  return (next: string | null, picturesSettled: boolean) => {
    if (picturesSettled) preloading = next;
    return next === preloading ? next : null;
  };
}
