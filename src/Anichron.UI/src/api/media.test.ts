import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { server } from '../test/server';
import { createMedia } from './media';

describe('loading a picture', () => {
  const url = '/api/v1/proxies/picture.webp';

  it('answers true once the picture is fetched and decoded', async () => {
    server.use(http.get(url, () => new HttpResponse(new Blob(['picture']))));

    expect(await createMedia().loadPicture(url)).toBe(true);
  });

  it('answers false when the picture cannot be fetched', async () => {
    server.use(http.get(url, () => HttpResponse.error()));

    expect(await createMedia().loadPicture(url)).toBe(false);
  });
});

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

    expect(await createMedia().loadImage(url)).toBe(true);
    expect(decoded).toEqual([url]);
  });

  it('answers false when the image cannot be decoded', async () => {
    vi.spyOn(HTMLImageElement.prototype, 'decode').mockRejectedValue(new Error('broken'));

    expect(await createMedia().loadImage(url)).toBe(false);
  });
});
