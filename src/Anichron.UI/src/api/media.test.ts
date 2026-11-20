import { http, HttpResponse } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { freedSources, keptSource, memoryUrls } from '../test/object-urls';
import { server } from '../test/server';
import { createMedia, type Wanted } from './media';

const renewNothing = () => Promise.resolve();

describe('loading an image', () => {
  const url = '/api/v1/proxies/picture.webp';

  it('decodes the image at the url, without a separate request, and answers true', async () => {
    const decoded: string[] = [];
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockImplementation(function (
      this: HTMLImageElement,
    ) {
      decoded.push(this.getAttribute('src') ?? '');
      return Promise.resolve();
    });

    expect(await createMedia(renewNothing).loadImage(url)).toBe(true);
    expect(decoded).toEqual([url]);
  });

  it('answers false when the image cannot be decoded', async () => {
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockRejectedValue(new Error('broken'));

    expect(await createMedia(renewNothing).loadImage(url)).toBe(false);
  });
});

describe('downloading media', () => {
  const at = (name: string) => `/api/v1/proxies/${name}`;
  let asked: string[];
  let answers: Map<string, () => Response | Promise<Response>>;
  let aborted: string[];

  function wants(pictures: readonly string[], videos: readonly string[] = []): Wanted {
    return [
      ...pictures.map((url) => ({ url, kind: 'picture' as const })),
      ...videos.map((url) => ({ url, kind: 'video' as const })),
    ];
  }

  function downloads(renew = () => Promise.resolve()) {
    return createMedia(renew, memoryUrls()).downloads;
  }

  async function settle() {
    await vi.advanceTimersByTimeAsync(10);
  }

  beforeEach(() => {
    asked = [];
    aborted = [];
    answers = new Map();
    server.use(
      http.get('/api/v1/proxies/:name', ({ request }) => {
        const { pathname } = new URL(request.url);
        asked.push(pathname);
        request.signal.addEventListener('abort', () => {
          aborted.push(pathname);
        });
        return (answers.get(pathname) ?? (() => new HttpResponse(new Uint8Array([1, 2]))))();
      }),
    );
  });

  function held(name: string) {
    let release: (response: Response) => void = () => undefined;
    answers.set(
      at(name),
      () =>
        new Promise<Response>((resolve) => {
          release = resolve;
        }),
    );
    return (response = new HttpResponse(new Uint8Array([1]))) => {
      release(response);
    };
  }

  it('downloads one at a time, the pictures in order before the videos', async () => {
    const store = downloads();
    const releaseFirst = held('first');

    store.keep(wants([at('first'), at('second')], [at('video')]));
    await settle();
    expect(asked).toEqual([at('first')]);

    releaseFirst();
    await settle();
    expect(asked).toEqual([at('first'), at('second'), at('video')]);
    expect(store.sourceOf(at('first'))).toBe(keptSource(at('first')));
    expect(store.sourceOf(at('video'))).toBe(keptSource(at('video')));
    expect(store.snapshot().picturesSettled).toBe(true);
  });

  it('stops a later download for an earlier one that is not there yet, and downloads it again after', async () => {
    const store = downloads();
    held('video');
    store.keep(wants([at('first')], [at('video')]));
    await settle();
    expect(asked).toEqual([at('first'), at('video')]);

    store.keep(wants([at('first'), at('second')], [at('video')]));
    await settle();

    expect(aborted).toEqual([at('video')]);
    expect(store.sourceOf(at('second'))).toBe(keptSource(at('second')));
    expect(asked).toEqual([at('first'), at('video'), at('second'), at('video')]);
    expect(store.failed(at('video'))).toBe(false);
  });

  it('keeps the download running when nothing earlier is missing', async () => {
    const store = downloads();
    held('second');
    store.keep(wants([at('first'), at('second')]));
    await settle();

    store.keep(wants([at('first'), at('second'), at('third')]));
    await settle();

    expect(aborted).toEqual([]);
    expect(asked).toEqual([at('first'), at('second')]);
  });

  it('downloads only the picture asked for, and nothing once cleared', async () => {
    const fetching = vi.spyOn(globalThis, 'fetch');
    const store = downloads();

    await store.picture(at('first'));
    store.clear();

    expect(store.snapshot().picturesSettled).toBe(true);
    await settle();
    expect(fetching).toHaveBeenCalledOnce();
  });

  it('has its pictures settled while a video still downloads', async () => {
    const store = downloads();
    held('video');

    store.keep(wants([at('first')], [at('video')]));
    await settle();

    expect(asked).toEqual([at('first'), at('video')]);
    expect(store.snapshot().picturesSettled).toBe(true);
  });

  it('decodes only pictures', async () => {
    const store = downloads();
    const decode = vi.spyOn(HTMLImageElement.prototype, 'decode');

    store.keep(wants([at('first')], [at('video')]));
    await settle();

    expect(store.sourceOf(at('video'))).toBe(keptSource(at('video')));
    expect(decode).toHaveBeenCalledOnce();
  });

  it.each([
    ['its type', { 'Content-Type': 'image/webp' }, 'image/webp'],
    ['no type', {}, ''],
  ])('keeps all of a download, with %s', async (_, headers, type) => {
    const blobs: Blob[] = [];
    const store = createMedia(renewNothing, {
      create: (blob) => {
        blobs.push(blob);
        return 'kept';
      },
      free: () => undefined,
    }).downloads;
    answers.set(at('first'), () => {
      const response = new HttpResponse(
        new ReadableStream({
          start(controller) {
            controller.enqueue(new Uint8Array([1]));
            controller.enqueue(new Uint8Array([2]));
            controller.close();
          },
        }),
        { headers },
      );
      return response;
    });
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockResolvedValue();

    await store.picture(at('first'));

    expect(blobs).toHaveLength(1);
    expect((blobs[0] as Blob).type).toBe(type);
    expect([...new Uint8Array(await (blobs[0] as Blob).arrayBuffer())]).toEqual([1, 2]);
  });

  it('serves a picture wanted again from the cache, and never frees it while wanted', async () => {
    const store = downloads();
    store.keep(wants([at('first')]));
    await settle();
    store.clear();
    store.keep(wants([at('first')]));

    for (let index = 0; index < 61; index++) {
      store.keep(wants([at('first'), at(`later-${String(index)}`)]));
      await settle();
    }
    store.keep(wants([at('first')]));

    expect(freedSources()).toEqual([keptSource(at('later-0'))]);
    expect(store.sourceOf(at('first'))).toBe(keptSource(at('first')));
  });

  it('has no source and its pictures unsettled until they arrive', async () => {
    const store = downloads();
    const release = held('first');

    store.keep(wants([at('first')]));
    await settle();

    expect(store.sourceOf(at('first'))).toBeNull();
    expect(store.snapshot().picturesSettled).toBe(false);
    release();
    await settle();
    expect(store.snapshot().picturesSettled).toBe(true);
  });

  it('answers a picture once downloaded and decoded, ahead of the rest', async () => {
    const store = downloads();
    const decoded: string[] = [];
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockImplementation(function (
      this: HTMLImageElement,
    ) {
      decoded.push(this.getAttribute('src') ?? '');
      return Promise.resolve();
    });
    store.keep(wants([at('second')]));

    const loaded = store.picture(at('first'));
    await settle();

    expect(await loaded).toBe(true);
    expect(asked).toEqual([at('first'), at('second')]);
    expect(decoded).toContain(keptSource(at('first')));
    expect(store.sourceOf(at('first'))).toBe(keptSource(at('first')));
  });

  it.each([
    ['refused', () => new HttpResponse(null, { status: 503 })],
    ['not sent', () => HttpResponse.error()],
  ])('fails a download the server %s, and tries it again on request', async (_, answer) => {
    const store = downloads();
    answers.set(at('first'), answer);
    const changes = vi.fn();
    store.subscribe(changes);

    const loaded = store.picture(at('first'));
    await settle();

    expect(await loaded).toBe(false);
    expect(store.failed(at('first'))).toBe(true);
    expect(store.sourceOf(at('first'))).toBeNull();
    expect(store.snapshot().picturesSettled).toBe(true);
    expect(changes).toHaveBeenCalled();

    answers.delete(at('first'));
    changes.mockClear();
    store.retry(at('first'));
    expect(changes).toHaveBeenCalledOnce();
    expect(store.snapshot().picturesSettled).toBe(false);
    await settle();
    expect(store.failed(at('first'))).toBe(false);
    expect(store.sourceOf(at('first'))).toBe(keptSource(at('first')));
    expect(asked).toEqual([at('first'), at('first')]);
  });

  it('tries nothing again for a download that has not failed', async () => {
    const store = downloads();
    store.keep(wants([at('first')]));
    await settle();

    store.retry(at('first'));
    store.retry(at('unknown'));
    await settle();

    expect(asked).toEqual([at('first')]);
  });

  it('fails a picture that cannot be decoded, and frees it', async () => {
    const store = downloads();
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockRejectedValue(new Error('broken'));

    expect(await store.picture(at('first'))).toBe(false);
    expect(store.failed(at('first'))).toBe(true);
    expect(freedSources()).toEqual([keptSource(at('first'))]);
  });

  it('renews the media session once on a 401 and downloads again', async () => {
    const renew = vi.fn(() => Promise.resolve());
    const store = downloads(renew);
    let refusals = 1;
    answers.set(at('first'), () =>
      refusals-- > 0
        ? new HttpResponse(null, { status: 401 })
        : new HttpResponse(new Uint8Array([1])),
    );

    expect(await store.picture(at('first'))).toBe(true);
    expect(renew).toHaveBeenCalledOnce();
    expect(asked).toEqual([at('first'), at('first')]);
  });

  it('fails a download still refused after renewing', async () => {
    const renew = vi.fn(() => Promise.resolve());
    const store = downloads(renew);
    answers.set(at('first'), () => new HttpResponse(null, { status: 401 }));

    expect(await store.picture(at('first'))).toBe(false);
    expect(renew).toHaveBeenCalledOnce();
  });

  it('aborts a download no longer kept, and moves on', async () => {
    const store = downloads();
    held('second');
    store.keep(wants([at('second'), at('third')]));
    await settle();
    const dropped = store.picture(at('second'));

    store.keep(wants([at('third')]));
    await settle();

    expect(aborted).toEqual([at('second')]);
    expect(freedSources()).toEqual([]);
    expect(await dropped).toBe(false);
    expect(store.sourceOf(at('second'))).toBeNull();
    expect(store.sourceOf(at('third'))).toBe(keptSource(at('third')));
  });

  it('keeps what arrived once no longer wanted, and serves it again without downloading', async () => {
    const store = downloads();
    store.keep(wants([at('first')], [at('video')]));
    await settle();

    store.clear();
    expect(store.sourceOf(at('first'))).toBeNull();
    expect(await store.picture(at('first'))).toBe(true);
    store.keep(wants([at('first')], [at('video')]));
    await settle();

    expect(store.sourceOf(at('first'))).toBe(keptSource(at('first')));
    expect(store.sourceOf(at('video'))).toBe(keptSource(at('video')));
    expect(asked).toEqual([at('first'), at('video')]);
    expect(freedSources()).toEqual([]);
  });

  it('frees the oldest beyond 60 pictures and 10 videos no longer wanted', async () => {
    const store = downloads();
    const names = (kind: string, count: number) =>
      Array.from({ length: count }, (_, index) => at(`${kind}-${String(index)}`));
    const pictures = names('picture', 61);
    const videos = names('video', 11);
    for (const [index, url] of pictures.entries()) {
      store.keep(wants([url], index < videos.length ? [videos[index] as string] : []));
      await settle();
    }
    store.clear();
    await settle();

    expect(freedSources()).toEqual([keptSource(at('video-0')), keptSource(at('picture-0'))]);
  });

  it('frees a download that arrives after it was dropped', async () => {
    const store = downloads();
    const release = held('first');
    vi.spyOn(AbortController.prototype, 'abort').mockImplementation(() => undefined);
    store.keep(wants([at('first')]));
    await settle();

    store.clear();
    release();
    await settle();

    expect(freedSources()).toEqual([keptSource(at('first'))]);
    expect(store.sourceOf(at('first'))).toBeNull();
  });

  it('tells its listeners of every piece that arrives, with the same snapshot', async () => {
    const store = downloads();
    const changes = vi.fn();
    answers.set(
      at('first'),
      () =>
        new HttpResponse(
          new ReadableStream({
            start(controller) {
              controller.enqueue(new Uint8Array([1]));
              controller.enqueue(new Uint8Array([2]));
            },
          }),
        ),
    );
    store.keep(wants([at('first')]));
    const before = store.snapshot();
    store.subscribe(changes);
    await settle();

    expect(changes).toHaveBeenCalledTimes(2);
    expect(store.snapshot()).toBe(before);
  });

  it('tells of what it keeps and what arrives, with a new snapshot each time, until told to stop', async () => {
    const store = downloads();
    const changes = vi.fn();
    const stop = store.subscribe(changes);
    const before = store.snapshot();

    store.keep(wants([at('first')]));
    expect(changes).toHaveBeenCalledOnce();
    const kept = store.snapshot();
    expect(kept).not.toBe(before);
    expect(kept.sourceOf(at('first'))).toBeNull();
    await settle();
    expect(changes).toHaveBeenCalledTimes(3);
    expect(store.snapshot()).not.toBe(kept);
    expect(store.snapshot().sourceOf(at('first'))).toBe(keptSource(at('first')));
    expect(kept.sourceOf(at('first'))).toBeNull();

    store.keep(wants([at('first')]));
    expect(changes).toHaveBeenCalledTimes(3);
    stop();
    store.clear();
    expect(changes).toHaveBeenCalledTimes(3);
  });

  it('makes object urls in the browser and revokes them', async () => {
    const create = vi
      .spyOn(URL, 'createObjectURL')
      .mockImplementation(() => `blob:${String(create.mock.calls.length)}`);
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const store = createMedia(() => Promise.resolve()).downloads;

    expect(await store.picture(at('first'))).toBe(true);
    expect(store.sourceOf(at('first'))).toBe('blob:1');
    expect(create.mock.calls[0]?.[0]).toBeInstanceOf(Blob);
    for (let index = 0; index < 60; index++) await store.picture(at(`later-${String(index)}`));
    store.clear();
    expect(revoke.mock.calls).toEqual([['blob:1']]);
  });
});
