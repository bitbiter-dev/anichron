import { decode } from 'blurhash';

const longSide = 32;

export function blurhashSize(width: number | null, height: number | null) {
  const [across, down] = [width ?? 1, height ?? 1];
  const scale = longSide / Math.max(across, down);
  return { width: Math.ceil(across * scale), height: Math.ceil(down * scale) };
}

export function paintBlurhash(canvas: HTMLCanvasElement | null, hash: string) {
  if (!canvas) return;
  const context = canvas.getContext('2d') as CanvasRenderingContext2D;
  const image = context.createImageData(canvas.width, canvas.height);
  image.data.set(decode(hash, canvas.width, canvas.height));
  context.putImageData(image, 0, 0);
}
