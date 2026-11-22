import { decoded } from '../media';
import { useRecovering } from '../use-recovering';

interface ProxyImageProps {
  src: string | null;
  className?: string;
  onDecoded?: () => void;
  onFailed?: () => void;
}

const nothing = () => undefined;

export function ProxyImage({
  src,
  className,
  onDecoded = nothing,
  onFailed = nothing,
}: ProxyImageProps) {
  const { reloads, onError } = useRecovering(onFailed);
  if (!src) return null;
  return (
    <img
      key={reloads}
      src={src}
      alt=""
      className={className}
      onError={() => {
        onError(src);
      }}
      onLoad={(event) => {
        decoded(event.currentTarget, onDecoded);
      }}
    />
  );
}
