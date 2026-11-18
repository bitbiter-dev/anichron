export interface Wanted {
  pictures: readonly string[];
  videos: readonly string[];
}

export interface Arrived {
  sourceOf: (url: string) => string | null;
  picturesSettled: boolean;
}

export interface Downloads {
  keep: (wanted: Wanted) => void;
  picture: (url: string) => Promise<boolean>;
  sourceOf: (url: string) => string | null;
  failed: (url: string) => boolean;
  retry: (url: string) => void;
  snapshot: () => Arrived;
  subscribe: (listener: () => void) => () => void;
  onProgress: (listener: () => void) => () => void;
  clear: () => void;
}

export interface ObjectUrls {
  create(blob: Blob, url: string): string;
  free(source: string): void;
}

export interface Media {
  loadImage(url: string): Promise<boolean>;
  downloads: Downloads;
}

function browserObjectUrls(): ObjectUrls {
  return {
    create: (blob) => URL.createObjectURL(blob),
    free: (source) => {
      URL.revokeObjectURL(source);
    },
  };
}

type DownloadState = 'waiting' | 'loading' | 'done' | 'failed';

interface Download {
  url: string;
  picture: boolean;
  state: DownloadState;
  source?: string;
  controller?: AbortController;
  settled: Promise<boolean>;
  settle: (done: boolean) => void;
}

function newDownload(url: string, picture: boolean): Download {
  let settle: (done: boolean) => void = () => undefined;
  const settled = new Promise<boolean>((resolve) => {
    settle = resolve;
  });
  return { url, picture, state: 'waiting', settled, settle };
}

async function decodeSource(source: string) {
  const image = new Image();
  image.src = source;
  await image.decode();
}

function createDownloads(renew: () => Promise<void>, objectUrls: ObjectUrls): Downloads {
  const kept = new Map<string, Download>();
  const retained = new Map<string, Download>();
  const limits = new Map([
    [true, 60],
    [false, 10],
  ]);
  let wanted: Wanted = { pictures: [], videos: [] };
  let order: string[] = [];
  let active: Download | undefined;
  const listeners = new Set<() => void>();
  const progressListeners = new Set<() => void>();

  function arrived(): Arrived {
    return {
      sourceOf: (url) => kept.get(url)?.source ?? null,
      picturesSettled: order.every((url) => {
        const { picture, state } = kept.get(url) as Download;
        return !picture || state === 'done' || state === 'failed';
      }),
    };
  }

  let snapshot = arrived();

  function changed() {
    snapshot = arrived();
    for (const listener of listeners) listener();
  }

  function progressed() {
    for (const listener of progressListeners) listener();
  }

  async function body(url: string, signal: AbortSignal) {
    const request = () => fetch(new URL(url, location.origin), { signal });
    let response = await request();
    if (response.status === 401) {
      await renew();
      response = await request();
    }
    if (!response.ok || !response.body) return null;
    const chunks: BlobPart[] = [];
    const reader = response.body.getReader();
    for (let read = await reader.read(); !read.done; read = await reader.read()) {
      chunks.push(read.value);
      progressed();
    }
    return new Blob(chunks, { type: response.headers.get('Content-Type') ?? '' });
  }

  async function load(download: Download, signal: AbortSignal) {
    const blob = await body(download.url, signal);
    if (!blob) return null;
    const source = objectUrls.create(blob, download.url);
    if (download.picture)
      await decodeSource(source).catch((error: unknown) => {
        objectUrls.free(source);
        throw error;
      });
    return source;
  }

  function finish(download: Download, source: string | null) {
    if (active === download) active = undefined;
    if (kept.get(download.url) !== download) {
      if (source) objectUrls.free(source);
      return;
    }
    download.state = source ? 'done' : 'failed';
    if (source) download.source = source;
    download.settle(Boolean(source));
    changed();
    pump();
  }

  function pump() {
    if (active) return;
    const next = order
      .map((url) => kept.get(url) as Download)
      .find(({ state }) => state === 'waiting');
    if (!next) return;
    const controller = new AbortController();
    active = next;
    next.state = 'loading';
    next.controller = controller;
    load(next, controller.signal).then(
      (source) => {
        finish(next, source);
      },
      () => {
        finish(next, null);
      },
    );
  }

  function evict() {
    for (const [picture, limit] of limits) {
      const old = [...retained.values()].filter((download) => download.picture === picture);
      for (const { url, source } of old.slice(0, Math.max(0, old.length - limit))) {
        retained.delete(url);
        objectUrls.free(source as string);
      }
    }
  }

  function drop(download: Download) {
    kept.delete(download.url);
    if (download.state === 'done') {
      retained.set(download.url, download);
      evict();
      return;
    }
    download.controller?.abort();
    download.settle(false);
    if (active === download) active = undefined;
  }

  function keep(next: Wanted) {
    const kinds = new Map<string, boolean>();
    for (const url of next.videos) kinds.set(url, false);
    for (const url of next.pictures) kinds.set(url, true);
    const dropped = [...kept.values()].filter(({ url }) => !kinds.has(url));
    const added = [...kinds].filter(([url]) => !kept.has(url));
    for (const download of dropped) drop(download);
    for (const [url, picture] of added) {
      kept.set(url, retained.get(url) ?? newDownload(url, picture));
      retained.delete(url);
    }
    wanted = next;
    order = [...new Set([...next.pictures, ...next.videos])];
    if (dropped.length + added.length > 0) changed();
    pump();
  }

  return {
    keep,
    picture(url) {
      keep({ pictures: [url, ...wanted.pictures], videos: wanted.videos });
      return (kept.get(url) as Download).settled;
    },
    sourceOf: (url) => kept.get(url)?.source ?? null,
    failed: (url) => kept.get(url)?.state === 'failed',
    retry(url) {
      const download = kept.get(url);
      if (download?.state !== 'failed') return;
      kept.set(url, newDownload(url, download.picture));
      pump();
    },
    snapshot: () => snapshot,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    onProgress(listener) {
      progressListeners.add(listener);
      return () => progressListeners.delete(listener);
    },
    clear() {
      keep({ pictures: [], videos: [] });
    },
  };
}

async function decodeImage(url: string) {
  const image = new Image();
  image.src = url;
  await image.decode();
  return true;
}

export function createMedia(
  renew: () => Promise<void>,
  objectUrls: ObjectUrls = browserObjectUrls(),
): Media {
  return {
    loadImage: (url) => decodeImage(url).catch(() => false),
    downloads: createDownloads(renew, objectUrls),
  };
}
