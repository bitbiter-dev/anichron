import { paintBlurhash } from '../blurhash';

interface BlurhashProps {
  hash: string;
  width: number;
  height: number;
}

export function Blurhash({ hash, width, height }: BlurhashProps) {
  return (
    <canvas
      ref={(canvas) => {
        paintBlurhash(canvas, hash);
      }}
      width={width}
      height={height}
      className="absolute inset-0 size-full object-contain"
    />
  );
}
