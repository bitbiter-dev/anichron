export type MediaKind = 'picture' | 'video';

export type Wanted = readonly { url: string; kind: MediaKind }[];

export interface Downloaded {
  sourceOf: (url: string | null) => string | null;
  picturesSettled: boolean;
}

export interface Downloads {
  keep: (wanted: Wanted) => void;
  picture: (url: string) => Promise<boolean>;
  sourceOf: (url: string | null) => string | null;
  failed: (url: string | null) => boolean;
  retry: (url: string | null) => void;
  snapshot: () => Downloaded;
  subscribe: (listener: () => void) => () => void;
  clear: () => void;
}

export interface ObjectUrls {
  create(blob: Blob, url: string): string;
  free(source: string): void;
  isObjectUrl(source: string): boolean;
}

export type Renewal = (source: string) => Promise<boolean>;

export interface Media {
  loadImage(url: string): Promise<boolean>;
  downloads: Downloads;
  renewOnce: () => Renewal;
}

function browserObjectUrls(): ObjectUrls {
  return {
    create: (blob) => URL.createObjectURL(blob),
    free: (source) => {
      URL.revokeObjectURL(source);
    },
    isObjectUrl: (source) => source.startsWith('blob:'),
  };
}

function renewingOnce(renew: () => Promise<void>, objectUrls: ObjectUrls) {
  return (): Renewal => {
    let renewed = false;
    return async (source) => {
      if (renewed || objectUrls.isObjectUrl(source)) return false;
      renewed = true;
      await renew();
      return true;
    };
  };
}

interface Download {
  url: string;
  kind: MediaKind;
  source: string | null;
  failed: boolean;
  controller?: AbortController;
  settled: Promise<boolean>;
  settle: (done: boolean) => void;
}

function newDownload(url: string, kind: MediaKind): Download {
  let settle: (done: boolean) => void = () => undefined;
  const settled = new Promise<boolean>((resolve) => {
    settle = resolve;
  });
  return { url, kind, source: null, failed: false, settled, settle };
}

async function decodeSource(source: string) {
  const image = new Image();
  image.src = source;
  await image.decode();
}

function createDownloads(renewOnce: () => Renewal, objectUrls: ObjectUrls): Downloads {
  const cacheLimits: Record<MediaKind, number> = { picture: 60, video: 10 };
  const wanted = new Map<string | null, Download>();
  const cached = new Map<string, Download>();
  let order: Wanted = [];
  let active: Download | undefined;
  const listeners = new Set<() => void>();
  const sourceOf = (url: string | null) => wanted.get(url)?.source ?? null;
  const settled = ({ source, failed }: Download) => source !== null || failed;

  function downloaded(): Downloaded {
    const sources = new Map([...wanted].map(([url, { source }]) => [url, source]));
    return {
      sourceOf: (url) => sources.get(url) ?? null,
      picturesSettled: [...wanted.values()].every(
        (download) => download.kind === 'video' || settled(download),
      ),
    };
  }

  let snapshot = downloaded();

  function notify() {
    for (const listener of listeners) listener();
  }

  function changed() {
    snapshot = downloaded();
    notify();
  }

  async function body(url: string, signal: AbortSignal) {
    const request = () => fetch(new URL(url, location.origin), { signal });
    let response = await request();
    if (response.status === 401 && (await renewOnce()(url))) response = await request();
    if (!response.ok || !response.body) return null;
    const chunks: BlobPart[] = [];
    const reader = response.body.getReader();
    for (let read = await reader.read(); !read.done; read = await reader.read()) {
      chunks.push(read.value);
      notify();
    }
    return new Blob(chunks, { type: response.headers.get('Content-Type') ?? '' });
  }

  async function load(download: Download, signal: AbortSignal) {
    const blob = await body(download.url, signal);
    if (!blob) return null;
    const source = objectUrls.create(blob, download.url);
    if (download.kind === 'picture')
      await decodeSource(source).catch((error: unknown) => {
        objectUrls.free(source);
        throw error;
      });
    return source;
  }

  function finish(download: Download, controller: AbortController, source: string | null) {
    if (download.controller !== controller) {
      if (source) objectUrls.free(source);
      return;
    }
    active = undefined;
    download.source = source;
    download.failed = !source;
    download.settle(Boolean(source));
    changed();
    startNext();
  }

  function stop(download: Download) {
    download.controller?.abort();
    download.controller = undefined;
  }

  function startNext() {
    const next = order
      .map(({ url }) => wanted.get(url) as Download)
      .find((download) => !settled(download));
    if (!next || next === active) return;
    if (active) stop(active);
    const controller = new AbortController();
    active = next;
    next.controller = controller;
    load(next, controller.signal).then(
      (source) => {
        finish(next, controller, source);
      },
      () => {
        finish(next, controller, null);
      },
    );
  }

  function evict() {
    for (const [kind, limit] of Object.entries(cacheLimits)) {
      const old = [...cached.values()].filter((download) => download.kind === kind);
      for (const { url, source } of old.slice(0, Math.max(0, old.length - limit))) {
        cached.delete(url);
        objectUrls.free(source as string);
      }
    }
  }

  function drop(download: Download) {
    wanted.delete(download.url);
    if (download.source) {
      cached.set(download.url, download);
      evict();
      return;
    }
    stop(download);
    download.settle(false);
  }

  function keep(next: Wanted) {
    const kinds = new Map(next.map(({ url, kind }) => [url, kind]));
    const dropped = [...wanted.values()].filter(({ url }) => !kinds.has(url));
    const added = [...kinds].filter(([url]) => !wanted.has(url));
    for (const download of dropped) drop(download);
    for (const [url, kind] of added) {
      wanted.set(url, cached.get(url) ?? newDownload(url, kind));
      cached.delete(url);
    }
    order = next;
    if (dropped.length + added.length > 0) changed();
    startNext();
  }

  return {
    keep,
    picture(url) {
      keep([{ url, kind: 'picture' }, ...order]);
      return (wanted.get(url) as Download).settled;
    },
    sourceOf,
    failed: (url) => wanted.get(url)?.failed === true,
    retry(url) {
      const download = wanted.get(url);
      if (!download?.failed) return;
      wanted.set(download.url, newDownload(download.url, download.kind));
      changed();
      startNext();
    },
    snapshot: () => snapshot,
    subscribe(listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    clear() {
      keep([]);
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
  const renewOnce = renewingOnce(renew, objectUrls);
  return {
    loadImage: (url) => decodeImage(url).catch(() => false),
    downloads: createDownloads(renewOnce, objectUrls),
    renewOnce,
  };
}
