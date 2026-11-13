import { decode } from 'blurhash';
import type { Memory } from '../../api/flashbacks';

const longSide = 32;

function blurhashSize(width: number | null, height: number | null) {
  const [across, down] = [width ?? 1, height ?? 1];
  const scale = longSide / Math.max(across, down);
  return { width: Math.ceil(across * scale), height: Math.ceil(down * scale) };
}

export function blurhashOf({ blurhash, width, height }: Memory) {
  return blurhash ? { hash: blurhash, ...blurhashSize(width, height) } : null;
}

export function paintBlurhash(canvas: HTMLCanvasElement | null, hash: string) {
  if (!canvas) return;
  const context = canvas.getContext('2d') as CanvasRenderingContext2D;
  const image = context.createImageData(canvas.width, canvas.height);
  image.data.set(decode(hash, canvas.width, canvas.height));
  context.putImageData(image, 0, 0);
}
