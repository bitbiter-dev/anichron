import type { Memory } from '../../api/flashbacks';
import type { Downloads, Wanted } from '../../api/media';
import { kindOf } from './memory-kinds';
import { kindInStory, type Place, type StoryState } from './story';

type MemoryAt = (place: Place) => Memory;
type PreviewAt = (place: Place) => string | null;
type FramesAt = (place: Place) => readonly Memory[];

export function wantedOf(
  state: StoryState,
  previewAt: PreviewAt,
  memoryAt: MemoryAt,
  framesAt: FramesAt,
): Wanted {
  const picturesOf = (urls: readonly (string | null)[]) =>
    urls.flatMap((url) => (url ? [{ url, kind: 'picture' as const }] : []));
  const pictures = (places: readonly Place[]) => picturesOf(places.map(previewAt));
  const frames = state.details ? framesAt(state).map(({ proxies }) => proxies.preview) : [];
  const videoOf = (place: Place) => {
    const memory = memoryAt(place);
    return kindInStory(kindOf(memory)).downloadsVideo ? memory.livePhotoVideoUrl : null;
  };
  const videos = (urls: readonly (string | null)[]) =>
    urls.flatMap((url) => (url ? [{ url, kind: 'video' as const }] : []));
  const current = videoOf(state);
  return [
    ...pictures([state]),
    ...picturesOf(frames),
    ...videos(state.play.state === 'still' ? [] : [current]),
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
      if (kindInStory(state.kind).loadsPreview && downloads.failed(url)) failed(state.visit);
    },
  };
}

export function nextVideo({ upcoming }: StoryState, memoryAt: MemoryAt) {
  const video = upcoming.map(memoryAt).find((memory) => kindInStory(kindOf(memory)).warmsVideo);
  return video?.proxies.video ?? null;
}

export function createPreload() {
  let preloading: string | null = null;
  return (next: string | null, picturesSettled: boolean) => {
    if (picturesSettled) preloading = next;
    return next === preloading ? next : null;
  };
}
