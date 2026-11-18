import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
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
