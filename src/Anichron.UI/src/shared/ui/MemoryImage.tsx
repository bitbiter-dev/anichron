import { Blurhash } from './BlurhashCanvas';
import { ProxyImage } from './ProxyImage';

interface MemoryImageProps {
  label: string;
  blurhash: { hash: string; width: number; height: number } | null;
  src: string | null;
  standIn?: string | null;
  onDecoded?: () => void;
  onFailed?: () => void;
  className: string;
}

export function MemoryImage({
  label,
  blurhash,
  standIn = null,
  className,
  ...image
}: MemoryImageProps) {
  return (
    <div role="img" aria-label={label} className={className}>
      {blurhash && <Blurhash {...blurhash} />}
      <ProxyImage src={standIn} className="absolute inset-0 size-full object-contain" />
      <ProxyImage {...image} className="relative size-full object-contain" />
    </div>
  );
}
