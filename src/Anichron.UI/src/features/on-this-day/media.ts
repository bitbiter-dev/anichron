import type { Downloads } from '../../api/media';

export function decoded(image: HTMLImageElement, ready: () => void) {
  void image.decode().then(ready, ready);
}

export function shownSource(downloads: Pick<Downloads, 'sourceOf' | 'failed'>, url: string) {
  return downloads.sourceOf(url) ?? (downloads.failed(url) ? url : null);
}
