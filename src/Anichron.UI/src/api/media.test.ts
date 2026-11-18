import { http, HttpResponse } from 'msw';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { createMedia } from './media';

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
  let freed: string[];
  let answers: Map<string, () => Response | Promise<Response>>;
  let aborted: string[];

  const memoryUrls = {
    create: (_: Blob, url: string) => `memory:${url}`,
    free: (source: string) => {
      freed.push(source);
    },
  };

  function downloads(renew = () => Promise.resolve()) {
    return createMedia(renew, memoryUrls).downloads;
  }

  async function settle() {
    await vi.advanceTimersByTimeAsync(10);
  }

  beforeEach(() => {
    asked = [];
    freed = [];
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

    store.keep({ pictures: [at('first'), at('second')], videos: [at('video')] });
    await settle();
    expect(asked).toEqual([at('first')]);

    releaseFirst();
    await settle();
    expect(asked).toEqual([at('first'), at('second'), at('video')]);
    expect(store.sourceOf(at('first'))).toBe(`memory:${at('first')}`);
    expect(store.sourceOf(at('video'))).toBe(`memory:${at('video')}`);
    expect(store.snapshot().picturesSettled).toBe(true);
  });

  it('has no source and its pictures unsettled until they arrive', async () => {
    const store = downloads();
    const release = held('first');

    store.keep({ pictures: [at('first')], videos: [] });
    await settle();

    expect(store.sourceOf(at('first'))).toBeNull();
    expect(store.snapshot().picturesSettled).toBe(false);
    release();
    await settle();
    expect(store.snapshot().picturesSettled).toBe(true);
  });

  it('answers a picture once downloaded and decoded, keeping it first', async () => {
    const store = downloads();
    const decoded: string[] = [];
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockImplementation(function (
      this: HTMLImageElement,
    ) {
      decoded.push(this.getAttribute('src') ?? '');
      return Promise.resolve();
    });
    store.keep({ pictures: [at('second')], videos: [] });

    const loaded = store.picture(at('first'));
    await settle();

    expect(await loaded).toBe(true);
    expect(asked.slice(0, 1)).toEqual([at('second')]);
    expect(decoded).toContain(`memory:${at('first')}`);
    expect(store.sourceOf(at('first'))).toBe(`memory:${at('first')}`);
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
    store.retry(at('first'));
    await settle();
    expect(store.failed(at('first'))).toBe(false);
    expect(store.sourceOf(at('first'))).toBe(`memory:${at('first')}`);
    expect(asked).toEqual([at('first'), at('first')]);
  });

  it('tries nothing again for a download that has not failed', async () => {
    const store = downloads();
    store.keep({ pictures: [at('first')], videos: [] });
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
    expect(freed).toEqual([`memory:${at('first')}`]);
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
    store.keep({ pictures: [at('second'), at('third')], videos: [] });
    await settle();
    const dropped = store.picture(at('second'));

    store.keep({ pictures: [at('third')], videos: [] });
    await settle();

    expect(aborted).toEqual([at('second')]);
    expect(await dropped).toBe(false);
    expect(store.sourceOf(at('second'))).toBeNull();
    expect(store.sourceOf(at('third'))).toBe(`memory:${at('third')}`);
  });

  it('keeps what arrived once no longer wanted, and serves it again without downloading', async () => {
    const store = downloads();
    store.keep({ pictures: [at('first')], videos: [at('video')] });
    await settle();

    store.clear();
    expect(store.sourceOf(at('first'))).toBeNull();
    expect(await store.picture(at('first'))).toBe(true);
    store.keep({ pictures: [at('first')], videos: [at('video')] });
    await settle();

    expect(store.sourceOf(at('first'))).toBe(`memory:${at('first')}`);
    expect(store.sourceOf(at('video'))).toBe(`memory:${at('video')}`);
    expect(asked).toEqual([at('first'), at('video')]);
    expect(freed).toEqual([]);
  });

  it('frees the oldest beyond 60 pictures and 10 videos no longer wanted', async () => {
    const store = downloads();
    const names = (kind: string, count: number) =>
      Array.from({ length: count }, (_, index) => at(`${kind}-${String(index)}`));
    const pictures = names('picture', 61);
    const videos = names('video', 11);
    for (const [index, url] of pictures.entries()) {
      store.keep({
        pictures: [url],
        videos: index < videos.length ? [videos[index] as string] : [],
      });
      await settle();
    }
    store.clear();
    await settle();

    expect(freed).toEqual([`memory:${at('video-0')}`, `memory:${at('picture-0')}`]);
  });

  it('frees a download that arrives after it was dropped', async () => {
    const store = downloads();
    const release = held('first');
    vi.spyOn(AbortController.prototype, 'abort').mockImplementation(() => undefined);
    store.keep({ pictures: [at('first')], videos: [] });
    await settle();

    store.clear();
    release();
    await settle();

    expect(freed).toEqual([`memory:${at('first')}`]);
    expect(store.sourceOf(at('first'))).toBeNull();
  });

  it('tells about every piece that arrives, until told to stop', async () => {
    const store = downloads();
    const progress = vi.fn();
    const stop = store.onProgress(progress);
    answers.set(
      at('first'),
      () =>
        new HttpResponse(
          new ReadableStream({
            start(controller) {
              controller.enqueue(new Uint8Array([1]));
              controller.enqueue(new Uint8Array([2]));
              controller.close();
            },
          }),
        ),
    );

    await store.picture(at('first'));
    expect(progress).toHaveBeenCalledTimes(2);

    stop();
    store.retry(at('first'));
    store.clear();
    await store.picture(at('second'));
    expect(progress).toHaveBeenCalledTimes(2);
  });

  it('tells of what it keeps and what arrives, with a new snapshot, until told to stop', async () => {
    const store = downloads();
    const changes = vi.fn();
    const stop = store.subscribe(changes);
    const before = store.snapshot();

    store.keep({ pictures: [at('first')], videos: [] });
    expect(changes).toHaveBeenCalledOnce();
    const kept = store.snapshot();
    expect(kept).not.toBe(before);
    expect(kept.sourceOf(at('first'))).toBeNull();
    await settle();
    expect(changes).toHaveBeenCalledTimes(2);
    expect(store.snapshot()).not.toBe(kept);
    expect(store.snapshot().sourceOf(at('first'))).toBe(`memory:${at('first')}`);

    store.keep({ pictures: [at('first')], videos: [] });
    expect(changes).toHaveBeenCalledTimes(2);
    stop();
    store.clear();
    expect(changes).toHaveBeenCalledTimes(2);
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
