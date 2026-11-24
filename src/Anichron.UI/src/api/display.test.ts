import { afterEach, describe, expect, it, vi } from 'vitest';
import { browserDisplay, showsCompactPreviews } from './display';
import { fakeDisplay } from '../test/display';

class FakeImage {
  static decodes = true;
  static probed: string[] = [];
  src = '';

  decode() {
    FakeImage.probed.push(this.src);
    return FakeImage.decodes ? Promise.resolve() : Promise.reject(new Error('unsupported'));
  }
}

afterEach(() => {
  vi.unstubAllGlobals();
  FakeImage.decodes = true;
  FakeImage.probed = [];
});

describe('the browser display', () => {
  it('decodes JPEG XL when its probe image decodes', async () => {
    vi.stubGlobal('Image', FakeImage);

    expect(await browserDisplay().decodesJpegXl()).toBe(true);
    expect(FakeImage.probed).toEqual([expect.stringMatching(/^data:image\/jxl;base64,\/wo/)]);
  });

  it('does not decode JPEG XL when its probe image fails', async () => {
    vi.stubGlobal('Image', FakeImage);
    FakeImage.decodes = false;

    expect(await browserDisplay().decodesJpegXl()).toBe(false);
  });

  it.each([
    [390, 844],
    [844, 390],
  ])('measures the long edge of a %i × %i screen', (width, height) => {
    vi.stubGlobal('screen', { width, height });

    expect(browserDisplay().longEdge()).toBe(844);
  });
});

describe('compact previews', () => {
  it.each([
    [1023, true, true],
    [1024, true, false],
    [390, false, false],
  ])(
    'on a screen with a %i px long edge that decodes JPEG XL: %s, are shown: %s',
    async (longEdge, jpegXl, shown) => {
      expect(await showsCompactPreviews(fakeDisplay({ longEdge, jpegXl }))).toBe(shown);
    },
  );

  it('are not probed for on a large screen', async () => {
    const display = fakeDisplay({ longEdge: 1920, jpegXl: true });
    const probe = vi.spyOn(display, 'decodesJpegXl');

    await showsCompactPreviews(display);

    expect(probe).not.toHaveBeenCalled();
  });
});
