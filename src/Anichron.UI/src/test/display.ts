import type { Display } from '../api/display';

export function fakeDisplay({ jpegXl = false, longEdge = 1920 } = {}): Display {
  return {
    decodesJpegXl: () => Promise.resolve(jpegXl),
    longEdge: () => longEdge,
  };
}
