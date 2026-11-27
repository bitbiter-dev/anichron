import { describe, expect, it } from 'vitest';
import { forgetInChunks } from './forget';

describe('forgetInChunks', () => {
  it('sends no empty chunk after a full one', async () => {
    const sent: number[] = [];
    const ids = Array.from({ length: 2000 }, (_, index) => `id-${String(index)}`);

    await forgetInChunks(
      ids,
      (chunk) => {
        sent.push(chunk.length);
        return Promise.resolve(chunk);
      },
      () => undefined,
    );

    expect(sent).toEqual([1000, 1000]);
  });
});
